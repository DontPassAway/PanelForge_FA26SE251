using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Common;

/// <summary>
/// BR-12 / CF5 bước 2: nội dung chương (Scene, Page, Panel, Element, ScriptLine) chỉ sửa được khi chương PreProduction / InProduction.
/// Approved/Locked/Published là chỉ đọc; muốn sửa phải mở lại chương kèm lý do. Handler trả lỗi với
/// <c>ResultErrorCodes.ChapterReadOnly</c> (409, khác mã xung đột phiên bản để FE không mở hộp thoại tải lại).
/// </summary>
internal static class ChapterEditGuard
{
    public static Task<string?> ReadOnlyReasonAsync(IPanelForgeDbContext db, Guid chapterId, CancellationToken ct) =>
        ReasonAsync(db.Chapters.Where(c => c.Id == chapterId), ct);

    public static Task<string?> ReadOnlyReasonForPageAsync(IPanelForgeDbContext db, Guid pageId, CancellationToken ct) =>
        ReasonAsync(db.Chapters.Where(c => db.Pages.Any(p => p.Id == pageId && p.ChapterId == c.Id)), ct);

    /// <summary>Lý do chỉ đọc của chương đã tải sẵn; null nếu sửa được.</summary>
    public static string? ReadOnlyReason(Chapter chapter) => Reason(chapter.Status);

    private static async Task<string?> ReasonAsync(IQueryable<Chapter> chapter, CancellationToken ct) =>
        Reason(await chapter.AsNoTracking().Select(c => (ChapterStatus?)c.Status).FirstOrDefaultAsync(ct));

    // Không thấy chương thì để handler tự báo "không tồn tại" như cũ
    private static string? Reason(ChapterStatus? status) =>
        status is null or ChapterStatus.PreProduction or ChapterStatus.InProduction
            ? null
            : $"Chương đang ở trạng thái {status}, nội dung chỉ đọc (BR-12). Mở lại chương kèm lý do để sửa.";
}
