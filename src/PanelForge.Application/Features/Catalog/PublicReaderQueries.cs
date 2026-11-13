using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Models;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Chapters.Commands.SetChapterCover;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Application.Features.Series.Queries.GetSeriesCover;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Catalog;

// Họp tuần 5: Reader đọc nội dung theo lịch phát hành. Chỉ chương Published đã đến giờ (PublishedAt <= now) mới đọc được;
// ngoài ra trả 404 như chương không tồn tại để không lộ nội dung đang sản xuất hay đã lên lịch.

/// <param name="ImageUrl">Đường dẫn tương đối tới ảnh trang; null nếu trang chưa có artwork.</param>
/// <param name="IsLocked">Trang nằm sau paywall: thuộc chương trả phí và không phải trang xem trước.</param>
/// <param name="IsUnlocked">Người xem đã có quyền xem trang; false thì <paramref name="ImageUrl"/> luôn null (server không lộ ảnh).
/// Mở bằng cách mua cả chương (không bán trang lẻ).</param>
/// <param name="IsPreview">Trang xem trước của chương trả phí (ai cũng xem được).</param>
public sealed record PublicPageDto(
    Guid Id,
    int PageNumber,
    string? ImageUrl,
    bool IsLocked = false,
    bool IsUnlocked = true,
    bool IsPreview = false);

/// <summary>Một bản ngôn ngữ đã phát hành của chương: giá mở khóa (chương trả phí) và người xem đã có quyền đọc chưa.</summary>
public sealed record PublicChapterLanguageDto(string Language, decimal PriceVnd, bool IsUnlocked);

public sealed record PublicChapterReadDto(
    Guid Id,
    Guid SeriesId,
    string SeriesTitle,
    ReadingDirection ReadingDirection,
    decimal ChapterNumber,
    string? Title,
    DateTime? PublishedAt,
    IReadOnlyList<PublicPageDto> Pages,
    string? CoverUrl = null,
    bool IsLocked = false,
    decimal PriceVnd = 0m,
    bool IsUnlocked = true,
    bool IsFree = true,
    string Language = ContentLanguages.Original,
    IReadOnlyList<PublicChapterLanguageDto>? Languages = null);

// ─── Chapter + danh sách trang ────────────────────────────────────────────────

/// <param name="Language">Bản ngôn ngữ muốn đọc (vi/en/ja); null = bản gốc.</param>
public sealed record GetPublicChapterQuery(Guid ChapterId, string? Language = null) : IRequest<Result<PublicChapterReadDto>>;

public sealed class GetPublicChapterQueryHandler : IRequestHandler<GetPublicChapterQuery, Result<PublicChapterReadDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetPublicChapterQueryHandler(IPanelForgeDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public static string PageImageUrl(Guid chapterId, Guid pageId, string language) =>
        language == ContentLanguages.Original
            ? $"/api/catalog/chapters/{chapterId}/pages/{pageId}/image"
            : $"/api/catalog/chapters/{chapterId}/pages/{pageId}/image?lang={language}";

    public async Task<Result<PublicChapterReadDto>> Handle(GetPublicChapterQuery q, CancellationToken ct)
    {
        var language = ContentLanguages.Normalize(q.Language);
        if (language is null)
            return Result<PublicChapterReadDto>.Failure("Ngôn ngữ không được hỗ trợ.", ResultErrorCodes.Validation);

        var now = DateTime.UtcNow;
        var chapter = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == q.ChapterId && c.Status == ChapterStatus.Published && c.PublishedAt <= now)
            .Select(c => new
            {
                c.Id, c.SeriesId, SeriesTitle = c.Series.Title, c.Series.ReadingDirection,
                c.ChapterNumber, c.Title, c.PublishedAt, c.CoverImageSha256,
                c.IsLocked, c.PriceVnd, c.Series.WorkspaceId
            })
            .FirstOrDefaultAsync(ct);
        if (chapter is null) return NotFound();

        var userId = _currentUser.UserId;
        var available = await ChapterReadAccess.AvailableLanguagesAsync(_db, chapter.Id, ct);
        if (!available.Contains(language))
            return Result<PublicChapterReadDto>.Failure(ChapterReadAccess.LanguageUnavailableMessage, ResultErrorCodes.NotFound);

        var languages = new List<PublicChapterLanguageDto>();
        foreach (var lang in available)
        {
            var price = chapter.IsLocked
                ? await ChapterReadAccess.ChapterPriceAsync(_db, chapter.Id, chapter.PriceVnd, lang, ct)
                : 0m;
            var unlocked = await ChapterReadAccess.CanReadAsync(
                _db, userId, chapter.Id, chapter.WorkspaceId, chapter.IsLocked, ct, lang);
            languages.Add(new PublicChapterLanguageDto(lang, price, unlocked));
        }

        // Chương trả phí chưa mua bản ngôn ngữ này: vẫn trả danh sách trang, nhưng chỉ trang xem trước có ảnh.
        var current = languages.First(l => l.Language == language);

        var hasImage = language == ContentLanguages.Original
            ? (await _db.Pages.AsNoTracking()
                .Where(p => p.ChapterId == chapter.Id && _db.AssetReferences.Any(r => r.PageId == p.Id))
                .Select(p => p.Id).ToListAsync(ct)).ToHashSet()
            : (await _db.PageTranslations.AsNoTracking()
                .Where(t => t.ChapterId == chapter.Id && t.Language == language && !t.IsDeleted)
                .Select(t => t.PageId).ToListAsync(ct)).ToHashSet();

        // Trang ngoài phần xem trước của chương chưa mua: không trả đường dẫn ảnh (ảnh cũng 403).
        var previews = await ChapterReadAccess.ResolvePreviewPagesAsync(_db, chapter.Id, ct);

        var dto = new PublicChapterReadDto(
            chapter.Id, chapter.SeriesId, chapter.SeriesTitle, chapter.ReadingDirection, chapter.ChapterNumber,
            chapter.Title, chapter.PublishedAt,
            previews.Select(p =>
            {
                var isPreview = chapter.IsLocked && p.IsPreview;
                var isLocked = chapter.IsLocked && !p.IsPreview;
                var unlocked = !isLocked || current.IsUnlocked;
                return new PublicPageDto(
                    p.PageId, p.PageNumber,
                    hasImage.Contains(p.PageId) && unlocked ? PageImageUrl(chapter.Id, p.PageId, language) : null,
                    IsLocked: isLocked,
                    IsUnlocked: unlocked,
                    IsPreview: isPreview);
            }).ToList(),
            GetPublicChapterCoverQueryHandler.CoverUrl(chapter.Id, chapter.CoverImageSha256),
            IsLocked: chapter.IsLocked,
            PriceVnd: current.PriceVnd,
            IsUnlocked: current.IsUnlocked,
            IsFree: !chapter.IsLocked,
            Language: language,
            Languages: languages);
        return Result<PublicChapterReadDto>.Success(dto);
    }

    private static Result<PublicChapterReadDto> NotFound() =>
        Result<PublicChapterReadDto>.Failure("Không tìm thấy chương đã phát hành.", ResultErrorCodes.NotFound);
}

// ─── Ảnh bìa chương ───────────────────────────────────────────────────────────

/// <summary>Bìa chương cho Reader: cùng điều kiện phát hành với nội dung chương, ngoài ra 404.</summary>
public sealed record GetPublicChapterCoverQuery(Guid ChapterId) : IRequest<Result<SeriesCoverContent>>;

public sealed class GetPublicChapterCoverQueryHandler : IRequestHandler<GetPublicChapterCoverQuery, Result<SeriesCoverContent>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public GetPublicChapterCoverQueryHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    /// <summary>URL công khai; <c>v</c> đổi theo nội dung để cache không giữ bìa cũ.</summary>
    public static string? CoverUrl(Guid chapterId, string? sha256)
        => sha256 is null ? null : $"/api/catalog/chapters/{chapterId}/cover?v={sha256[..12]}";

    public async Task<Result<SeriesCoverContent>> Handle(GetPublicChapterCoverQuery q, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var sha = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == q.ChapterId && c.Status == ChapterStatus.Published && c.PublishedAt <= now)
            .Select(c => c.CoverImageSha256).FirstOrDefaultAsync(ct);
        if (sha is null) return NotFound();

        await using var stream = await _storage.OpenReadAsync(SetChapterCoverCommandHandler.StorageKey(q.ChapterId, sha), ct);
        if (stream is null) return NotFound();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        return Result<SeriesCoverContent>.Success(
            new SeriesCoverContent(content, AssetRules.DetectImageContentType(content) ?? "application/octet-stream"));
    }

    private static Result<SeriesCoverContent> NotFound() =>
        Result<SeriesCoverContent>.Failure("Không tìm thấy bìa chương đã phát hành.", ResultErrorCodes.NotFound);
}

// ─── Ảnh bìa bộ truyện ───────────────────────────────────────────────────────

/// <summary>Bìa Series cho Reader: chỉ Series đã có ít nhất một chương Published đến giờ (như catalog), ngoài ra 404.</summary>
public sealed record GetPublicSeriesCoverQuery(Guid SeriesId) : IRequest<Result<SeriesCoverContent>>;

public sealed class GetPublicSeriesCoverQueryHandler : IRequestHandler<GetPublicSeriesCoverQuery, Result<SeriesCoverContent>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public GetPublicSeriesCoverQueryHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    /// <summary>URL công khai; <c>v</c> đổi theo nội dung để cache không giữ bìa cũ.</summary>
    public static string? CoverUrl(Guid seriesId, string? sha256)
        => sha256 is null ? null : $"/api/catalog/series/{seriesId}/cover?v={sha256[..12]}";

    public async Task<Result<SeriesCoverContent>> Handle(GetPublicSeriesCoverQuery q, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var sha = await _db.Series.AsNoTracking()
            .Where(s => s.Id == q.SeriesId
                        && s.Chapters.Any(c => c.Status == ChapterStatus.Published && c.PublishedAt <= now))
            .Select(s => s.CoverImageSha256).FirstOrDefaultAsync(ct);
        if (sha is null) return NotFound();

        await using var stream = await _storage.OpenReadAsync(SetSeriesCoverCommandHandler.StorageKey(q.SeriesId, sha), ct);
        if (stream is null) return NotFound();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        return Result<SeriesCoverContent>.Success(
            new SeriesCoverContent(content, AssetRules.DetectImageContentType(content) ?? "application/octet-stream"));
    }

    private static Result<SeriesCoverContent> NotFound() =>
        Result<SeriesCoverContent>.Failure("Không tìm thấy bìa bộ truyện đã phát hành.", ResultErrorCodes.NotFound);
}

// ─── Ảnh một trang ────────────────────────────────────────────────────────────

/// <summary>
/// Ảnh Reader thấy = trang hoàn chỉnh theo <see cref="FinalArtwork"/> (ảnh cả trang, hoặc ghép ảnh từng Panel vào khung)
/// kèm lớp chữ — cùng kết quả với xem thử và xuất file. Chương chỉ Published sau khi mọi việc Approved.
/// </summary>
/// <param name="Language">Bản ngôn ngữ (vi = ảnh Studio; en/ja = ảnh dịch Publisher upload); null = bản gốc.</param>
public sealed record GetPublicPageImageQuery(Guid ChapterId, Guid PageId, string? Language = null)
    : IRequest<Result<AssetDownloadDto>>;

public sealed class GetPublicPageImageQueryHandler : IRequestHandler<GetPublicPageImageQuery, Result<AssetDownloadDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;
    private readonly IPageRepository _pages;
    private readonly IPageLetteringCompositor _compositor;
    private readonly ICurrentUserService _currentUser;

    public GetPublicPageImageQueryHandler(
        IPanelForgeDbContext db,
        IAssetStorage storage,
        IPageRepository pages,
        IPageLetteringCompositor compositor,
        ICurrentUserService currentUser)
    {
        _db = db;
        _storage = storage;
        _pages = pages;
        _compositor = compositor;
        _currentUser = currentUser;
    }

    public async Task<Result<AssetDownloadDto>> Handle(GetPublicPageImageQuery q, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var chapter = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == q.ChapterId && c.Status == ChapterStatus.Published && c.PublishedAt <= now)
            .Select(c => new { c.Id, c.IsLocked, c.Series.WorkspaceId })
            .FirstOrDefaultAsync(ct);
        if (chapter is null) return NotFound();

        var language = ContentLanguages.Normalize(q.Language);
        if (language is null) return NotFound();

        var pageExists = await _db.Pages.AsNoTracking().AnyAsync(p => p.Id == q.PageId && p.ChapterId == q.ChapterId, ct);
        if (!pageExists) return NotFound();
        if (!await ChapterReadAccess.CanViewPageAsync(
                _db, _currentUser.UserId, chapter.Id, q.PageId, chapter.WorkspaceId, chapter.IsLocked, language, ct))
            return Result<AssetDownloadDto>.Failure(ChapterReadAccess.LockedPageMessage, ResultErrorCodes.Forbidden);

        var image = await LoadImageAsync(_db, _storage, _pages, _compositor, q.ChapterId, q.PageId, language, ct);
        return image is null ? NotFound() : Result<AssetDownloadDto>.Success(image);
    }

    /// <summary>
    /// Ảnh một trang theo ngôn ngữ (không kiểm quyền — người gọi tự kiểm): vi = ảnh Studio ghép lớp chữ,
    /// en/ja = ảnh dịch Publisher upload. Null nếu chưa có ảnh.
    /// </summary>
    public static async Task<AssetDownloadDto?> LoadImageAsync(
        IPanelForgeDbContext db, IAssetStorage storage, IPageRepository pages, IPageLetteringCompositor compositor,
        Guid chapterId, Guid pageId, string language, CancellationToken ct)
    {
        if (language != ContentLanguages.Original)
        {
            var translation = await db.PageTranslations.AsNoTracking()
                .Where(t => t.PageId == pageId && t.ChapterId == chapterId && t.Language == language && !t.IsDeleted)
                .Select(t => new { t.ContentSha256, t.ContentType, t.Page.PageNumber })
                .FirstOrDefaultAsync(ct);
            if (translation is null) return null;
            var stream = await storage.OpenReadAsync(
                PageTranslation.StorageKey(pageId, language, translation.ContentSha256), ct);
            return stream is null
                ? null
                : new AssetDownloadDto(stream, translation.ContentType, $"trang-{translation.PageNumber}-{language}");
        }

        var artwork = (await FinalArtwork.SelectAsync(db, chapterId, ct, pageId)).FirstOrDefault();
        if (artwork is null) return null;

        var image = await PageImageBuilder.BuildAsync(
            db, storage, pages, compositor, pageId, artwork.AssetReferenceIds.ToList(), ct);
        return image is null
            ? null
            : new AssetDownloadDto(new MemoryStream(image.Content), image.ContentType, $"trang-{image.PageNumber}.png");
    }

    private static Result<AssetDownloadDto> NotFound() =>
        Result<AssetDownloadDto>.Failure("Không tìm thấy ảnh trang đã phát hành.", ResultErrorCodes.NotFound);
}
