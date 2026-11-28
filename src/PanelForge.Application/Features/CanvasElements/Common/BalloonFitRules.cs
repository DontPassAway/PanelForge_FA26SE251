using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.CanvasElements.Common;

/// <summary>
/// ART-06 / BR-27.2 ⚠️ Check Balloon Fit: khi tranh dưới bóng thoại đổi (Ink/Color mới, Artist quay lại Pencil), bật cờ trên các
/// khung đang có bóng thoại để người làm chữ kiểm tra lại vị trí bóng. Khung không có bóng thì không bật (không có gì để kiểm).
/// </summary>
public static class BalloonFitRules
{
    public const string PendingMessage = "khung cần kiểm tra lại vị trí bóng thoại sau khi tranh đổi (Check Balloon Fit)";

    /// <summary>
    /// Bật cờ trên các khung của trang có bóng thoại; <paramref name="panelId"/> khác null thì chỉ xét khung đó.
    /// Bóng thuộc khung theo PanelId của element hoặc theo khung của dòng thoại đã gắn; bóng chưa thuộc khung nào tính cho mọi khung.
    /// Panel trả về đang được theo dõi, người gọi tự SaveChanges.
    /// </summary>
    public static async Task<IReadOnlyList<Panel>> FlagAsync(
        IPanelForgeDbContext db, IPageRepository pages, Guid pageId, Guid? panelId, CancellationToken ct)
    {
        var page = await pages.GetAsync(pageId, ct);
        var balloons = page?.Elements.Values
            .Where(e => !e.IsRemoved && Page.TextElementTypes.Contains(e.ElementType)).ToList() ?? [];
        if (balloons.Count == 0) return [];

        var lineIds = balloons.Where(e => e.PanelId is null && e.ScriptLineId.HasValue).Select(e => e.ScriptLineId!.Value).ToList();
        var linePanels = lineIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await db.ScriptLines.AsNoTracking().Where(l => lineIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.PanelId, ct);

        var withBalloon = new HashSet<Guid>();
        var unplaced = false;
        foreach (var e in balloons)
        {
            var owner = e.PanelId ?? (e.ScriptLineId is { } id && linePanels.TryGetValue(id, out var p) ? p : null);
            if (owner is { } o) withBalloon.Add(o);
            else unplaced = true;
        }

        var panels = await db.Panels
            .Where(p => p.PageId == pageId && (panelId == null || p.Id == panelId))
            .ToListAsync(ct);
        var flagged = panels.Where(p => unplaced || withBalloon.Contains(p.Id)).ToList();
        foreach (var panel in flagged) panel.RequestBalloonCheck();
        return flagged;
    }

    /// <summary>Lý do chặn (duyệt, nộp chữ) khi trang còn khung chưa xác nhận kiểm tra bóng thoại; null nếu không còn.</summary>
    public static async Task<string?> PendingReasonAsync(IPanelForgeDbContext db, Guid pageId, CancellationToken ct)
    {
        var pending = await db.Panels.AsNoTracking()
            .CountAsync(p => p.PageId == pageId && p.BalloonCheckRequestedAt != null, ct);
        if (pending == 0) return null;
        var pageNumber = await db.Pages.AsNoTracking().Where(p => p.Id == pageId).Select(p => p.PageNumber).FirstOrDefaultAsync(ct);
        return $"Trang {pageNumber} còn {pending} {PendingMessage}.";
    }
}
