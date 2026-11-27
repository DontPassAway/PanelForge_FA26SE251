using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Catalog;

/// <summary>Trang có thuộc phần xem trước (N trang đầu của ấn bản ngôn ngữ, BR-33) hay không.</summary>
public sealed record ResolvedPreviewPage(Guid PageId, int PageNumber, bool IsPreview);

/// <summary>
/// BR-33: điều khoản thương mại của một ấn bản ngôn ngữ do Publisher đặt.
/// <paramref name="IsConfigured"/> = false (D4: chưa cấu hình) → khóa toàn bộ và không mua được.
/// </summary>
public sealed record EditionTerms(bool IsPaid, decimal PriceVnd, int FreePreviewPages, bool IsConfigured)
{
    public static readonly EditionTerms Unconfigured = new(true, 0m, 0, false);

    /// <summary>Người chưa mở khóa bị chặn ngoài phần xem trước (Trả phí hoặc chưa cấu hình).</summary>
    public bool IsLocked => IsPaid || !IsConfigured;

    /// <summary>Có thể mở khóa bằng tiền: đã cấu hình Trả phí với giá &gt; 0.</summary>
    public bool IsPurchasable => IsConfigured && IsPaid && PriceVnd > 0;
}

/// <summary>
/// Quyền đọc ấn bản ngôn ngữ trên các API catalog công khai. Mua ấn bản mở mọi trang của đúng ngôn ngữ đó;
/// người chưa mua chỉ xem N trang đầu, N = <c>ChapterEdition.FreePreviewPages</c> (BR-33). Ấn bản Free mở toàn bộ.
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
    /// BR-34 / D9b: Các bản ngôn ngữ đã phát hành của chương dựa trên ChapterEdition Published.
    /// Trả rỗng nếu chương chưa phát hành hoặc chưa đến giờ hẹn (PublishedAt &gt; utcNow).
    /// </summary>
    public static async Task<IReadOnlyList<string>> AvailableLanguagesAsync(
        IPanelForgeDbContext db, Guid chapterId, CancellationToken ct, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var chapter = await db.Chapters.AsNoTracking()
            .Where(c => c.Id == chapterId && !c.IsDeleted)
            .Select(c => new { c.Status, c.PublishedAt, OriginalLanguage = c.Series.OriginalLanguage })
            .FirstOrDefaultAsync(ct);

        if (chapter is null
            || chapter.Status != ChapterStatus.HandedOffAndPublished
            || chapter.PublishedAt is null
            || chapter.PublishedAt > now)
        {
            return [];
        }

        var publishedLangs = await db.ChapterEditions.AsNoTracking()
            .Where(e => e.ChapterId == chapterId && e.Status == EditionStatus.Published && !e.IsDeleted)
            .Select(e => e.LanguageCode)
            .ToListAsync(ct);

        if (publishedLangs.Count == 0)
        {
            var origLang = ContentLanguages.Normalize(chapter.OriginalLanguage) ?? ContentLanguages.Original;
            return [origLang];
        }

        return publishedLangs;
    }

    // ─── Điều khoản thương mại của ấn bản (BR-33) ──────────────────────────────

    /// <summary>
    /// Loại Free/Trả phí, giá và N trang xem trước của ấn bản (chương, ngôn ngữ) — nguồn duy nhất cho đọc, giá, mở khóa.
    /// Ấn bản chưa có hoặc chưa cấu hình (D4) → <see cref="Unconfigured"/>: khóa toàn bộ và không mua được (fail-closed).
    /// </summary>
    public static async Task<EditionTerms> EditionTermsAsync(
        IPanelForgeDbContext db, Guid chapterId, string language, CancellationToken ct)
    {
        var edition = await db.ChapterEditions.AsNoTracking()
            .Where(e => e.ChapterId == chapterId && e.LanguageCode == language && !e.IsDeleted)
            .Select(e => new { e.IsPaid, e.PriceVnd, e.FreePreviewPages, e.MonetizationConfiguredAt })
            .FirstOrDefaultAsync(ct);
        return edition?.MonetizationConfiguredAt is null
            ? EditionTerms.Unconfigured
            : new EditionTerms(edition.IsPaid, edition.PriceVnd, edition.FreePreviewPages, true);
    }

    /// <summary>Điều khoản của mọi ấn bản các chương (một truy vấn) — cho danh sách chương / catalog / thống kê.</summary>
    public static async Task<IReadOnlyDictionary<(Guid ChapterId, string Language), EditionTerms>> EditionTermsAsync(
        IPanelForgeDbContext db, IReadOnlyCollection<Guid> chapterIds, CancellationToken ct)
    {
        var rows = await db.ChapterEditions.AsNoTracking()
            .Where(e => chapterIds.Contains(e.ChapterId) && !e.IsDeleted)
            .Select(e => new { e.ChapterId, e.LanguageCode, e.IsPaid, e.PriceVnd, e.FreePreviewPages, e.MonetizationConfiguredAt })
            .ToListAsync(ct);
        return rows.ToDictionary(
            r => (r.ChapterId, r.LanguageCode),
            r => r.MonetizationConfiguredAt is null
                ? EditionTerms.Unconfigured
                : new EditionTerms(r.IsPaid, r.PriceVnd, r.FreePreviewPages, true));
    }

    public static EditionTerms TermsOf(
        IReadOnlyDictionary<(Guid ChapterId, string Language), EditionTerms> terms, Guid chapterId, string language) =>
        terms.TryGetValue((chapterId, language), out var t) ? t : EditionTerms.Unconfigured;

    // ─── Trang xem trước ─────────────────────────────────────────────────────

    /// <summary>
    /// Trang xem trước hiệu lực của mọi trang trong chương ở một ngôn ngữ: N trang đầu của ấn bản đó (BR-33).
    /// Chỉ có ý nghĩa với ấn bản Trả phí — ấn bản Free mở toàn bộ.
    /// </summary>
    public static async Task<IReadOnlyList<ResolvedPreviewPage>> ResolvePreviewPagesAsync(
        IPanelForgeDbContext db, Guid chapterId, string language, CancellationToken ct)
    {
        var terms = await EditionTermsAsync(db, chapterId, language, ct);
        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId)
            .OrderBy(p => p.PageNumber)
            .Select(p => new { p.Id, p.PageNumber })
            .ToListAsync(ct);

        return Resolve(terms.FreePreviewPages, pages.Select(p => (p.Id, p.PageNumber)).ToList());
    }

    /// <summary>
    /// Quy tắc thuần (không DB): N trang đầu là xem trước. Luôn chừa ít nhất một trang khóa (D4: N &lt; tổng số trang),
    /// kể cả khi chương bị mở lại và bớt trang sau lúc Publisher đặt N.
    /// </summary>
    public static IReadOnlyList<ResolvedPreviewPage> Resolve(
        int freePreviewPages, IReadOnlyList<(Guid Id, int PageNumber)> orderedPages)
    {
        var previewCount = Math.Min(Math.Max(0, freePreviewPages), Math.Max(0, orderedPages.Count - 1));
        return orderedPages.Select((p, i) => new ResolvedPreviewPage(p.Id, p.PageNumber, i < previewCount)).ToList();
    }

    /// <summary>
    /// Có được xem ảnh một trang ở ngôn ngữ này không: chương miễn phí / đã có quyền đọc chương → mọi trang;
    /// chưa mua → chỉ N trang đầu của ấn bản ngôn ngữ đó.
    /// </summary>
    public static async Task<bool> CanViewPageAsync(
        IPanelForgeDbContext db, Guid? userId, Guid chapterId, Guid pageId, Guid workspaceId,
        string language, CancellationToken ct)
    {
        var terms = await EditionTermsAsync(db, chapterId, language, ct);
        if (await CanReadAsync(db, userId, chapterId, workspaceId, terms.IsLocked, ct, language)) return true;
        var previews = await ResolvePreviewPagesAsync(db, chapterId, language, ct);
        return previews.Any(p => p.PageId == pageId && p.IsPreview);
    }

    /// <summary>
    /// Có được đọc toàn bộ ấn bản ngôn ngữ này không. <paramref name="isLocked"/> = <see cref="EditionTerms.IsLocked"/>
    /// của đúng ấn bản (ấn bản Free luôn đọc được).
    /// </summary>
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
