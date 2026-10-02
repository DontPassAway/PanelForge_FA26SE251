using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Common;

internal static class AiSnapshotBuilder
{
    private static readonly string[] TextElementMarkers = ["BALLOON", "NARRATION", "CAPTION", "TEXT"];

    /// <summary>
    /// Chụp context của run. Mọi loại workflow (trừ Producer): Bible hiệu lực tại Chapter (BR-15: revision có EffectiveFromChapterNumber ≤ chương
    /// hiện tại, lớn nhất rồi đến VersionNumber lớn nhất) + revision IDs + script nền của Scene. Thêm theo loại: layout đã Accept (Editor/Artist/Letterer),
    /// panel + style rules (Artist), thoại + panel + bubble + giới hạn chữ ước lượng (Letterer), snapshot tiến độ do code tính (Producer).
    /// Trả null nếu Series chưa có Bible mà workflow cần Bible.
    /// </summary>
    public static async Task<RunSnapshot?> BuildAsync(
        IPanelForgeDbContext db, IPageRepository pages, Chapter chapter, Scene? scene, AiWorkflowType type, DateTime nowUtc,
        CancellationToken ct)
    {
        if (type == AiWorkflowType.Producer)
            return new RunSnapshot(chapter.ChapterNumber, "(cấp Chapter)", null, [], [],
                Progress: await AiProgressBuilder.BuildAsync(db, chapter, nowUtc, ct));
        if (scene is null) return null;

        var entries = await BuildBibleAsync(db, chapter, ct);
        if (entries is null && type != AiWorkflowType.Letterer) return null;
        entries ??= [];

        var lines = await db.ScriptLines.AsNoTracking()
            .Where(l => l.SceneId == scene.Id)
            .OrderBy(l => l.LineOrder)
            .Select(l => new BaselineScriptLine(l.Id, l.LineOrder, l.SpeakerCharacterId, l.DialogueText, l.StageDirection))
            .ToListAsync(ct);

        var snapshot = new RunSnapshot(chapter.ChapterNumber, scene.Heading ?? $"Scene {scene.SceneNumber}", scene.Summary, entries, lines);
        if (type is not (AiWorkflowType.Editor or AiWorkflowType.Artist or AiWorkflowType.Letterer)) return snapshot;

        var layoutRevision = await AiWorkflowFacts.LatestLayoutAsync(db, scene.Id, ct);
        var layout = layoutRevision is null ? null : AiContentValidator.TryParse<StoryboardLayout>(layoutRevision.PayloadJson, out _);
        snapshot = snapshot with { ExistingLayout = layout, LayoutRevisionId = layoutRevision?.Id };
        if (layoutRevision is null || layout is null) return snapshot;

        var mapping = AiLayoutMapping.Read(layoutRevision.MappingJson);
        var panelIds = mapping.Select(m => m.PanelId).ToList();
        var panels = await db.Panels.AsNoTracking().Where(p => panelIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        return type switch
        {
            AiWorkflowType.Artist => snapshot with { Artist = await BuildArtistAsync(db, chapter, layoutRevision.Id, layout, mapping, panels, entries, ct) },
            AiWorkflowType.Letterer => snapshot with { Lettering = await BuildLetteringAsync(pages, layoutRevision.Id, lines, mapping, panels, ct) },
            _ => snapshot
        };
    }

    private static async Task<List<BibleEntryContext>?> BuildBibleAsync(IPanelForgeDbContext db, Chapter chapter, CancellationToken ct)
    {
        var bible = await db.SeriesBibles.AsNoTracking()
            .Include(b => b.Entries).ThenInclude(e => e.Revisions)
            .FirstOrDefaultAsync(b => b.SeriesId == chapter.SeriesId, ct);
        if (bible is null) return null;

        var chapterInt = (int)Math.Floor(chapter.ChapterNumber);
        var entries = new List<BibleEntryContext>();
        foreach (var entry in bible.Entries.Where(e => e.IsActive).OrderBy(e => e.Category).ThenBy(e => e.Code))
        {
            var rev = entry.Revisions
                .Where(r => r.EffectiveFromChapterNumber <= chapterInt)
                .OrderByDescending(r => r.EffectiveFromChapterNumber)
                .ThenByDescending(r => r.VersionNumber)
                .FirstOrDefault();
            if (rev is null) continue;

            var (name, description) = (entry.Name, entry.Description);
            try
            {
                using var doc = JsonDocument.Parse(rev.SnapshotJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("Name", out var n) || root.TryGetProperty("name", out n)) name = n.GetString() ?? name;
                if (root.TryGetProperty("Description", out var d) || root.TryGetProperty("description", out d)) description = d.GetString() ?? description;
            }
            catch (JsonException) { /* snapshot hỏng → dùng trạng thái hiện tại của entry, giống GetBibleEntriesAtChapter */ }

            entries.Add(new BibleEntryContext(
                entry.Id, entry.Code, entry.Category.ToString(), name, description,
                rev.Id, rev.VersionNumber, rev.EffectiveFromChapterNumber));
        }
        return entries;
    }

    private static async Task<ArtistContext> BuildArtistAsync(
        IPanelForgeDbContext db, Chapter chapter, Guid layoutRevisionId, StoryboardLayout layout,
        IReadOnlyList<MappedPanel> mapping, Dictionary<Guid, Panel> alivePanels, IReadOnlyList<BibleEntryContext> bible,
        CancellationToken ct)
    {
        var validIds = bible.Select(b => b.EntryId).ToHashSet();
        var result = new List<ArtistPanelContext>();
        foreach (var page in layout.Pages)
            foreach (var p in page.Panels)
            {
                var m = mapping.FirstOrDefault(x => x.LayoutPageNumber == page.PageNumber && x.PanelNumber == p.PanelNumber);
                if (m is null || !alivePanels.ContainsKey(m.PanelId)) continue;
                result.Add(new ArtistPanelContext(m.PanelId, m.PageNumber, p.PanelNumber, p.ReadingOrder, p.Camera, p.Description,
                    p.CharacterIds.Where(validIds.Contains).ToList(), p.SourceScriptLineIds));
            }

        var rules = await db.ConsistencyRules.AsNoTracking()
            .Where(r => r.SeriesId == chapter.SeriesId && r.IsEnabled)
            .OrderBy(r => r.Name).Take(20)
            .Select(r => new StyleRuleContext(r.Name, r.RuleType, r.Description, r.Severity))
            .ToListAsync(ct);
        return new ArtistContext(layoutRevisionId, result.OrderBy(p => p.ReadingOrder).ToList(), rules);
    }

    private static async Task<LetteringContext> BuildLetteringAsync(
        IPageRepository pages, Guid layoutRevisionId, IReadOnlyList<BaselineScriptLine> lines,
        IReadOnlyList<MappedPanel> mapping, Dictionary<Guid, Panel> alivePanels, CancellationToken ct)
    {
        // Bubble/text element nằm trong Event Store (Marten), không có liên kết cứng tới ScriptLine: code ghép theo nội dung trùng
        // khớp đúng một lần (single-binding); không ghép được thì chỉ dùng giới hạn theo diện tích panel.
        var elementsByPage = new Dictionary<Guid, List<ElementState>>();
        foreach (var pageId in mapping.Select(m => m.PageId).Distinct())
        {
            var page = await pages.GetAsync(pageId, ct);
            if (page is null) continue;
            elementsByPage[pageId] = page.Elements.Values
                .Where(e => !e.IsRemoved && IsTextElement(e.ElementType))
                .OrderBy(e => e.ZIndex).ToList();
        }

        var used = new HashSet<Guid>();
        var result = new List<LetteringLineContext>();
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.Dialogue)).OrderBy(l => l.Order))
        {
            var m = mapping.FirstOrDefault(x => x.SourceScriptLineIds.Contains(line.LineId) && alivePanels.ContainsKey(x.PanelId));
            if (m is null) continue;   // thoại chưa nằm trong panel nào → chưa có chỗ để đặt chữ

            var dialogue = line.Dialogue!.Trim();
            ElementState? bound = null;
            if (elementsByPage.TryGetValue(m.PageId, out var candidates))
                bound = candidates.FirstOrDefault(e => !used.Contains(e.ElementId)
                    && Normalize(e.Content) == Normalize(dialogue) && e.Width > 0 && e.Height > 0);
            if (bound is not null) used.Add(bound.ElementId);

            var (maxChars, source) = bound is not null
                ? (LetteringLimits.ForElement(bound.Width, bound.Height), LetteringLimits.SourceElement)
                : (LetteringLimits.ForPanel(PanelWidth(alivePanels[m.PanelId]), PanelHeight(alivePanels[m.PanelId])), LetteringLimits.SourcePanel);
            result.Add(new LetteringLineContext(line.LineId, line.Order, line.SpeakerId, dialogue, m.PanelId, m.PageNumber, m.PanelNumber,
                bound?.ElementId, bound?.ElementType, maxChars, source));
        }
        return new LetteringContext(layoutRevisionId, result);
    }

    private static bool IsTextElement(string type) =>
        !type.Contains("SOUND", StringComparison.OrdinalIgnoreCase)
        && TextElementMarkers.Any(m => type.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string? s) =>
        string.Join(' ', (s ?? "").ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static double PanelWidth(Panel p) => ReadDouble(p.BoundingBox, "width");
    private static double PanelHeight(Panel p) => ReadDouble(p.BoundingBox, "height");

    private static double ReadDouble(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : 0;
        }
        catch (JsonException) { return 0; }
    }
}
