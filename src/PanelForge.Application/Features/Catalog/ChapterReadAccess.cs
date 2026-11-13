using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Catalog;

/// <summary>Trang xem trước hiệu lực của một trang (sau khi áp mặc định "N trang đầu" của Series).</summary>
public sealed record ResolvedPreviewPage(Guid PageId, int PageNumber, bool IsPreview);

/// <summary>
/// Quyền đọc nội dung chương trả phí (<c>Chapter.IsLocked</c>) trên các API catalog công khai. Mua chương mở mọi trang;
/// người chưa mua chỉ xem các trang xem trước (Publisher chọn, hoặc mặc định N trang đầu của Series). Chương miễn phí mở toàn bộ.
/// Được đọc khi: Publisher/Admin hệ thống, thành viên đang hoạt động của Studio sở hữu truyện,
/// VIP còn hạn, hoặc đã mua (entitlement) <b>đúng ngôn ngữ</b> đang đọc — mua theo từng bản ngôn ngữ.
/// Một nguồn duy nhất cho danh sách chương, nội dung chương, ảnh trang và mở khóa để các nơi không lệch nhau.
/// </summary>
public static class ChapterReadAccess
{
    public const string LockedMessage =
        "Chương truyện trả phí. Vui lòng đăng nhập và mở khóa chương (bằng Ví, VietQR hoặc gói VIP) để đọc.";

    public const string LockedPageMessage =
        "Trang thuộc chương trả phí. Vui lòng mở khóa chương (bằng Ví, VietQR hoặc gói VIP) để xem.";

    public const string LanguageUnavailableMessage = "Bản ngôn ngữ này của chương chưa phát hành.";

    /// <summary>Người xem có quyền đọc mọi chương của Studio mà không cần mua (Publisher/Admin, thành viên Studio).</summary>
    public static async Task<bool> IsStaffAsync(
        IPanelForgeDbContext db, Guid? userId, Guid workspaceId, CancellationToken ct)
    {
        if (!userId.HasValue) return false;

        var systemRole = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId.Value && u.IsActive)
            .Select(u => (SystemRole?)u.Role)
            .FirstOrDefaultAsync(ct);
        if (systemRole is SystemRole.Publisher or SystemRole.Admin) return true;

        return await db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m => m.UserId == userId.Value
                           && m.WorkspaceId == workspaceId
                           && m.Status == WorkspaceMemberStatus.Active, ct);
    }

    public static Task<bool> HasActiveVipAsync(IPanelForgeDbContext db, Guid userId, DateTime now, CancellationToken ct) =>
        db.ReaderWallets.AsNoTracking()
            .AnyAsync(w => w.UserId == userId && !w.IsDeleted && w.IsVip
                           && (w.VipExpiresAt == null || w.VipExpiresAt > now), ct);

    // ─── Ngôn ngữ & giá ───────────────────────────────────────────────────────

    /// <summary>
    /// Các bản ngôn ngữ đã phát hành của chương: luôn có bản gốc (vi); bản dịch khi mọi trang đều có ảnh dịch.
    /// </summary>
    public static async Task<IReadOnlyList<string>> AvailableLanguagesAsync(
        IPanelForgeDbContext db, Guid chapterId, CancellationToken ct)
    {
        var pageCount = await db.Pages.AsNoTracking().CountAsync(p => p.ChapterId == chapterId, ct);
        var translated = await db.PageTranslations.AsNoTracking()
            .Where(t => t.ChapterId == chapterId && !t.IsDeleted)
            .GroupBy(t => t.Language)
            .Select(g => new { Language = g.Key, Pages = g.Select(t => t.PageId).Distinct().Count() })
            .ToListAsync(ct);

        var result = new List<string> { ContentLanguages.Original };
        result.AddRange(ContentLanguages.Translations.Where(lang =>
            pageCount > 0 && translated.Any(t => t.Language == lang && t.Pages >= pageCount)));
        return result;
    }

    /// <summary>Giá mở khóa chương trả phí cho một ngôn ngữ (giá riêng của Publisher, không có thì giá chung).</summary>
    public static async Task<decimal> ChapterPriceAsync(
        IPanelForgeDbContext db, Guid chapterId, decimal basePriceVnd, string language, CancellationToken ct)
    {
        var custom = await db.ChapterLanguagePrices.AsNoTracking()
            .Where(p => p.ChapterId == chapterId && p.Language == language && !p.IsDeleted)
            .Select(p => (decimal?)p.PriceVnd)
            .FirstOrDefaultAsync(ct);
        return custom ?? basePriceVnd;
    }

    // ─── Trang xem trước ─────────────────────────────────────────────────────

    /// <summary>
    /// Trang xem trước hiệu lực của mọi trang trong chương: chương Publisher đã chọn tay → theo từng trang;
    /// ngược lại mặc định Series "N trang đầu" (luôn chừa ít nhất một trang khóa).
    /// Chỉ có ý nghĩa với chương trả phí — chương miễn phí mở toàn bộ.
    /// </summary>
    public static async Task<IReadOnlyList<ResolvedPreviewPage>> ResolvePreviewPagesAsync(
        IPanelForgeDbContext db, Guid chapterId, CancellationToken ct)
    {
        var chapter = await db.Chapters.AsNoTracking()
            .Where(c => c.Id == chapterId)
            .Select(c => new { c.PreviewPagesCustomized, c.Series.DefaultPreviewPageCount })
            .FirstOrDefaultAsync(ct);
        if (chapter is null) return [];

        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId)
            .OrderBy(p => p.PageNumber)
            .Select(p => new { p.Id, p.PageNumber, p.IsPreview })
            .ToListAsync(ct);

        return Resolve(chapter.PreviewPagesCustomized, chapter.DefaultPreviewPageCount,
            pages.Select(p => (p.Id, p.PageNumber, p.IsPreview)).ToList());
    }

    /// <summary>Quy tắc thuần (không DB) dùng chung cho đọc chương, ảnh trang và màn Publisher.</summary>
    public static IReadOnlyList<ResolvedPreviewPage> Resolve(
        bool customized, int defaultPreviewCount,
        IReadOnlyList<(Guid Id, int PageNumber, bool IsPreview)> orderedPages)
    {
        if (customized)
            return orderedPages.Select(p => new ResolvedPreviewPage(p.Id, p.PageNumber, p.IsPreview)).ToList();

        var previewCount = Math.Min(Math.Max(0, defaultPreviewCount), Math.Max(0, orderedPages.Count - 1));
        return orderedPages.Select((p, i) => new ResolvedPreviewPage(p.Id, p.PageNumber, i < previewCount)).ToList();
    }

    /// <summary>
    /// Có được xem ảnh một trang ở ngôn ngữ này không: chương miễn phí / đã có quyền đọc chương → mọi trang;
    /// chưa mua → chỉ trang xem trước.
    /// </summary>
    public static async Task<bool> CanViewPageAsync(
        IPanelForgeDbContext db, Guid? userId, Guid chapterId, Guid pageId, Guid workspaceId, bool chapterIsLocked,
        string language, CancellationToken ct)
    {
        if (await CanReadAsync(db, userId, chapterId, workspaceId, chapterIsLocked, ct, language)) return true;
        var previews = await ResolvePreviewPagesAsync(db, chapterId, ct);
        return previews.Any(p => p.PageId == pageId && p.IsPreview);
    }

    /// <summary>Có được đọc toàn bộ chương ở ngôn ngữ này không (chương không khóa luôn đọc được).</summary>
    public static async Task<bool> CanReadAsync(
        IPanelForgeDbContext db, Guid? userId, Guid chapterId, Guid workspaceId, bool isLocked, CancellationToken ct,
        string language = ContentLanguages.Original)
    {
        if (!isLocked) return true;
        if (!userId.HasValue) return false;
        if (await IsStaffAsync(db, userId, workspaceId, ct)) return true;
        if (await HasActiveVipAsync(db, userId.Value, DateTime.UtcNow, ct)) return true;

        return await db.ChapterEntitlements.AsNoTracking()
            .AnyAsync(e => e.UserId == userId.Value && e.ChapterId == chapterId && e.Language == language && !e.IsDeleted, ct);
    }
}
