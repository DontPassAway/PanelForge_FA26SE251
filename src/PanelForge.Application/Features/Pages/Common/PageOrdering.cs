using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Pages.Common;

/// <summary>
/// Đánh số lại trang trong một Chapter (di chuyển / chèn giữa) và giữ thứ tự cốt truyện:
/// đọc các Panel theo (số trang, thứ tự đọc) thì số Scene không được giảm — Scene liền mạch, không chồng lấn (V1.2).
/// </summary>
public static class PageOrdering
{
    // Số tạm cho read model để không đụng unique index (chapter_id, page_number) khi dời nhiều trang
    private const int TempOffset = 1_000_000;

    /// <summary>Thông báo lỗi nếu thứ tự trang mới làm đảo/cắt Scene; null nếu hợp lệ. Trang không có trong map giữ số cũ.</summary>
    public static async Task<string?> SceneOrderViolationAsync(
        IPanelForgeDbContext db, Guid chapterId, IReadOnlyDictionary<Guid, int> newNumbers, CancellationToken ct)
    {
        var panels = await (
            from p in db.Panels
            join pg in db.Pages on p.PageId equals pg.Id
            join s in db.Scenes on p.SceneId equals s.Id
            where pg.ChapterId == chapterId
            select new { p.PageId, pg.PageNumber, p.ReadingOrder, s.SceneNumber }).ToListAsync(ct);

        var ordered = panels
            .Select(x => (x.PageId, Page: newNumbers.GetValueOrDefault(x.PageId, x.PageNumber), x.ReadingOrder, x.SceneNumber))
            .OrderBy(x => x.Page).ThenBy(x => x.ReadingOrder)
            .ToList();
        for (var i = 1; i < ordered.Count; i++)
            if (ordered[i].SceneNumber < ordered[i - 1].SceneNumber)
                return $"Không thể sắp xếp như vậy: trang {ordered[i].Page} chứa khung của Cảnh {ordered[i].SceneNumber} " +
                       $"nhưng lại đứng sau khung của Cảnh {ordered[i - 1].SceneNumber} (trang {ordered[i - 1].Page}). " +
                       "Thứ tự trang phải giữ đúng thứ tự cảnh trong kịch bản.";
        return null;
    }

    /// <summary>
    /// Áp số trang mới: append PageReorderedEvent vào Marten cho từng trang đổi số, rồi cập nhật read model EF qua hai bước
    /// (số tạm → số thật). Gọi trong transaction của handler. Trả lỗi nếu một trang không có event stream.
    /// </summary>
    public static async Task<string?> ApplyAsync(
        IPanelForgeDbContext db, IPageRepository repo, Guid chapterId, IReadOnlyDictionary<Guid, int> newNumbers, string? userId, CancellationToken ct)
    {
        var ids = newNumbers.Keys.ToList();
        var rows = await db.Pages.Where(p => p.ChapterId == chapterId && ids.Contains(p.Id)).ToListAsync(ct);
        var changed = rows.Where(r => r.PageNumber != newNumbers[r.Id]).ToList();
        if (changed.Count == 0) return null;

        foreach (var row in changed)
        {
            var aggregate = await repo.GetAsync(row.Id, ct);
            if (aggregate is null) return $"Page {row.Id} không tồn tại.";
            aggregate.Reorder(newNumbers[row.Id]);
            if (userId is not null) aggregate.UpdatedBy = userId;
            await repo.SaveAsync(aggregate, ct);
        }

        foreach (var row in changed) row.Reorder(row.PageNumber + TempOffset);
        await db.SaveChangesAsync(ct);
        foreach (var row in changed)
        {
            row.Reorder(newNumbers[row.Id]);
            if (userId is not null) row.UpdatedBy = userId;
        }
        await db.SaveChangesAsync(ct);
        return null;
    }
}
