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
    /// <summary>
    /// Chụp context của run. Mọi loại workflow: Bible hiệu lực tại Chapter (BR-15: revision có EffectiveFromChapterNumber ≤ chương
    /// hiện tại, lớn nhất rồi đến VersionNumber lớn nhất; Nhân vật chỉ lấy bản đã khoá) + revision IDs + script nền của Scene. Thêm theo loại: layout đã Accept (Editor/Artist/Letterer),
    /// panel + style rules (Artist).
    /// Trả null nếu Series chưa có Bible mà workflow cần Bible.
    /// </summary>
    public static async Task<RunSnapshot?> BuildAsync(
        IPanelForgeDbContext db, IPageRepository pages, Chapter chapter, Scene? scene, AiWorkflowType type, DateTime nowUtc,
        CancellationToken ct, IReadOnlySet<Guid>? artistPanelIds = null)
    {
        if (scene is null) return null;

        var entries = await BuildBibleAsync(db, chapter, ct);
        if (entries is null) return null;

        var lines = await db.ScriptLines.AsNoTracking()
            .Where(l => l.SceneId == scene.Id)
            .OrderBy(l => l.LineOrder)
            .Select(l => new BaselineScriptLine(l.Id, l.LineOrder, l.SpeakerCharacterId, l.DialogueText, l.StageDirection))
            .ToListAsync(ct);

        var storyArc = await db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.StoryArc).FirstOrDefaultAsync(ct);
        var snapshot = new RunSnapshot(chapter.ChapterNumber, scene.Heading ?? $"Scene {scene.SceneNumber}", scene.Summary, entries, lines,
            SeriesStoryArc: storyArc, ChapterOutline: chapter.Outline);
        if (type is not (AiWorkflowType.Editor or AiWorkflowType.Artist)) return snapshot;

        var layoutRevision = await AiWorkflowFacts.LatestLayoutAsync(db, scene.Id, ct);
        var layout = layoutRevision is null ? null : AiContentValidator.TryParse<StoryboardLayout>(layoutRevision.PayloadJson, out _);
        snapshot = snapshot with { ExistingLayout = layout, LayoutRevisionId = layoutRevision?.Id };
        if (layoutRevision is null || layout is null) return snapshot;

        var mapping = AiLayoutMapping.Read(layoutRevision.MappingJson);
        var panelIds = mapping.Select(m => m.PanelId).ToList();
        var panels = await db.Panels.AsNoTracking().Where(p => panelIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        return type switch
        {
            AiWorkflowType.Artist => snapshot with { Artist = await BuildArtistAsync(db, chapter, layoutRevision.Id, layout, mapping, panels, entries, artistPanelIds, ct) },
            _ => snapshot
        };
    }

    internal static async Task<List<BibleEntryContext>?> BuildBibleAsync(IPanelForgeDbContext db, Chapter chapter, CancellationToken ct)
    {
        var bible = await db.SeriesBibles.AsNoTracking()
            .Include(b => b.Entries).ThenInclude(e => e.Revisions)
            .FirstOrDefaultAsync(b => b.SeriesId == chapter.SeriesId, ct);
        if (bible is null) return null;

        var chapterInt = (int)Math.Floor(chapter.ChapterNumber);
        var entries = new List<BibleEntryContext>();
        foreach (var entry in bible.Entries.Where(e => e.IsActive).OrderBy(e => e.Category).ThenBy(e => e.Code))
        {
            // V1.2: AI chỉ dựa trên character sheet đã được Producer khoá; nhân vật chưa khoá bị bỏ khỏi context.
            var rev = entry.EffectiveRevisionAt(chapterInt, canonOnly: true);
            if (rev is null) continue;

            var (name, description, details) = (entry.Name, entry.Description, entry.DetailsJson);
            try
            {
                using var doc = JsonDocument.Parse(rev.SnapshotJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("Name", out var n) || root.TryGetProperty("name", out n)) name = n.GetString() ?? name;
                if (root.TryGetProperty("Description", out var d) || root.TryGetProperty("description", out d)) description = d.GetString() ?? description;
                if ((root.TryGetProperty("DetailsJson", out var dj) || root.TryGetProperty("detailsJson", out dj)) && dj.ValueKind == JsonValueKind.String)
                    details = dj.GetString() ?? details;
            }
            catch (JsonException) { /* snapshot hỏng → dùng trạng thái hiện tại của entry, giống GetBibleEntriesAtChapter */ }

            // Sự thật có cấu trúc (diesAtChapter, availableFromChapter, knownBy...) cho Continuity Agent; "{}" coi như không có
            if (details is { Length: <= 2 }) details = null;

            entries.Add(new BibleEntryContext(
                entry.Id, entry.Code, entry.Category.ToString(), name, description,
                rev.Id, rev.VersionNumber, rev.EffectiveFromChapterNumber, details));
        }
        return entries;
    }

    private static async Task<ArtistContext> BuildArtistAsync(
        IPanelForgeDbContext db, Chapter chapter, Guid layoutRevisionId, StoryboardLayout layout,
        IReadOnlyList<MappedPanel> mapping, Dictionary<Guid, Panel> alivePanels, IReadOnlyList<BibleEntryContext> bible,
        IReadOnlySet<Guid>? onlyPanelIds, CancellationToken ct)
    {
        var validIds = bible.Select(b => b.EntryId).ToHashSet();
        var result = new List<ArtistPanelContext>();
        foreach (var page in layout.Pages)
            foreach (var p in page.Panels)
            {
                var m = mapping.FirstOrDefault(x => x.LayoutPageNumber == page.PageNumber && x.PanelNumber == p.PanelNumber);
                if (m is null || !alivePanels.ContainsKey(m.PanelId)) continue;
                // Soạn riêng vài panel: các panel khác giữ mô tả đã duyệt (gộp lại khi Accept)
                if (onlyPanelIds is not null && !onlyPanelIds.Contains(m.PanelId)) continue;
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
}
