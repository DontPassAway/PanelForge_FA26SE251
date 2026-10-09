using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.CanvasElements.Common;

public sealed record LineFacts(string? Text, Guid? Speaker, int SceneNumber, int LineOrder, Guid ChapterId);

public sealed record PageLetteringAnalysis(
    IReadOnlyDictionary<Guid, LineFacts> Lines,
    IReadOnlyList<ElementWarningDto> Warnings,
    IReadOnlySet<Guid> StaleElementIds)
{
    /// <summary>Lý do chặn nộp stage Letter (BR-05, BR-20), null nếu được phép. Thứ tự đọc và đè vùng chỉ cảnh báo, không chặn.</summary>
    public string? SubmissionBlockReason(int pageNumber)
    {
        var unacked = Warnings.Count(w => w.Code == LetteringWarningRules.TextOverflow && !w.Acknowledged);
        if (StaleElementIds.Count == 0 && unacked == 0) return null;

        var parts = new List<string>();
        if (StaleElementIds.Count > 0) parts.Add($"{StaleElementIds.Count} chữ lỗi thời so với script (Writer đã sửa thoại)");
        if (unacked > 0) parts.Add($"{unacked} cảnh báo tràn chữ chưa xác nhận kèm lý do");
        return $"Trang {pageNumber} chưa thể nộp Lettering: {string.Join("; ", parts)}.";
    }
}

public static class LetteringPageAnalysis
{
    public static async Task<PageLetteringAnalysis> AnalyzeAsync(IPanelForgeDbContext db, Page page, CancellationToken ct)
    {
        var active = page.Elements.Values.Where(e => !e.IsRemoved).ToList();
        var lineIds = active.Where(e => e.ScriptLineId.HasValue).Select(e => e.ScriptLineId!.Value).Distinct().ToList();

        var lines = lineIds.Count == 0
            ? new Dictionary<Guid, LineFacts>()
            : (await db.ScriptLines.AsNoTracking()
                .Where(l => lineIds.Contains(l.Id))
                .Select(l => new { l.Id, l.DialogueText, l.StageDirection, l.SpeakerCharacterId, l.LineOrder, l.Scene.SceneNumber, l.Scene.ChapterId })
                .ToListAsync(ct))
              .ToDictionary(l => l.Id, l => new LineFacts(l.DialogueText ?? l.StageDirection, l.SpeakerCharacterId, l.SceneNumber, l.LineOrder, l.ChapterId));

        // Lỗi thời: Writer đã sửa thoại/speaker, hoặc xóa dòng sau lúc gắn
        var stale = active
            .Where(e => e.ScriptLineId is { } id &&
                        (!lines.TryGetValue(id, out var line) || line.Text != e.SourceLineText || line.Speaker != e.SpeakerId))
            .Select(e => e.ElementId)
            .ToHashSet();

        var direction = await db.Series.AsNoTracking()
            .Where(s => db.Chapters.Any(c => c.Id == page.ChapterId && c.SeriesId == s.Id))
            .Select(s => (ReadingDirection?)s.ReadingDirection)
            .FirstOrDefaultAsync(ct) ?? ReadingDirection.LeftToRight;

        var warnings = LetteringWarningRules.Evaluate(
            active, direction, lines.ToDictionary(kv => kv.Key, kv => (kv.Value.SceneNumber, kv.Value.LineOrder)));

        return new PageLetteringAnalysis(lines, warnings, stale);
    }

    /// <summary>BR-05: dòng phải tồn tại, cùng Chapter với Page, và element phải là loại chữ. Trả (dòng, null) hoặc (null, lỗi).</summary>
    public static async Task<(LineFacts? Line, string? Error)> ResolveLineAsync(
        IPanelForgeDbContext db, Guid lineId, Page page, string elementType, CancellationToken ct)
    {
        if (!Page.TextElementTypes.Contains(elementType))
            return (null, $"Chỉ element chữ ({string.Join(", ", Page.TextElementTypes)}) mới gắn được ScriptLine.");

        var line = await db.ScriptLines.AsNoTracking()
            .Where(l => l.Id == lineId)
            .Select(l => new LineFacts(l.DialogueText ?? l.StageDirection, l.SpeakerCharacterId, l.Scene.SceneNumber, l.LineOrder, l.Scene.ChapterId))
            .FirstOrDefaultAsync(ct);

        if (line is null) return (null, $"ScriptLine {lineId} không tồn tại.");
        if (line.ChapterId != page.ChapterId) return (null, $"ScriptLine {lineId} không thuộc Chapter của Page {page.Id}.");
        return (line, null);
    }
}
