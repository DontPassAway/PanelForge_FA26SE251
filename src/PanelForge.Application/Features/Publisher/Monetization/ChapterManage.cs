using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Catalog;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Monetization;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

/// <param name="IsCustom">Publisher đặt giá riêng cho ngôn ngữ này; false = dùng giá chung của chương.</param>
public sealed record ChapterLanguagePriceDto(string Language, decimal PriceVnd, bool IsCustom);

/// <param name="TranslatedPages">Số trang đã có ảnh dịch (bản gốc vi = mọi trang).</param>
/// <param name="IsAvailable">Bản ngôn ngữ đã phát hành cho Reader (đủ ảnh mọi trang).</param>
public sealed record ChapterLanguageStatusDto(string Language, int TranslatedPages, bool IsAvailable);

/// <param name="IsPreview">Trang xem trước hiệu lực (sau khi áp mặc định Series nếu chương chưa chọn tay):
/// người chưa mua chương trả phí vẫn xem được; các trang khác khóa đến khi mua chương.</param>
/// <param name="ImageUrls">Ảnh xem trước cho Publisher theo ngôn ngữ (vi luôn có nếu trang có artwork; en/ja khi đã upload).</param>
public sealed record ManagedPageDto(
    Guid PageId,
    int PageNumber,
    bool IsPreview,
    IReadOnlyDictionary<string, string?> ImageUrls);

/// <param name="ChapterIsLocked">Chương trả phí: trang xem trước chỉ có tác dụng với chương trả phí; chương miễn phí mở toàn bộ.</param>
/// <param name="PreviewPagesCustomized">Publisher đã chọn tay trang xem trước (không theo mặc định Series).</param>
public sealed record ChapterManageDto(
    Guid ChapterId,
    Guid SeriesId,
    decimal ChapterNumber,
    string? Title,
    bool IsPublished,
    bool ChapterIsLocked,
    decimal BasePriceVnd,
    bool PreviewPagesCustomized,
    int SeriesDefaultPreviewPageCount,
    IReadOnlyList<ChapterLanguagePriceDto> LanguagePrices,
    IReadOnlyList<ChapterLanguageStatusDto> Languages,
    IReadOnlyList<ManagedPageDto> Pages);

/// <param name="PreviewPageIds">Các trang xem trước; trang không có trong danh sách bị khóa với người chưa mua.</param>
public sealed record SetChapterPreviewPagesRequest(IReadOnlyList<Guid>? PreviewPageIds);

/// <param name="PriceVnd">null hoặc 0 = bỏ giá riêng, dùng giá chung của chương.</param>
public sealed record LanguagePriceItem(string Language, decimal? PriceVnd);

public sealed record SetChapterLanguagePricesRequest(IReadOnlyList<LanguagePriceItem>? Prices);

// ─── Query: màn Manage của một chương ────────────────────────────────────────

public sealed record GetChapterManageQuery(Guid ChapterId) : IRequest<Result<ChapterManageDto>>;

public sealed class GetChapterManageQueryHandler : IRequestHandler<GetChapterManageQuery, Result<ChapterManageDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetChapterManageQueryHandler(IPanelForgeDbContext db) => _db = db;

    public Task<Result<ChapterManageDto>> Handle(GetChapterManageQuery q, CancellationToken ct) =>
        ChapterManageReader.ReadAsync(_db, q.ChapterId, ct);
}

public static class ChapterManageReader
{
    public static string PreviewImageUrl(Guid chapterId, Guid pageId, string language) =>
        $"/api/publisher/chapters/{chapterId}/pages/{pageId}/image?lang={language}";

    public static async Task<Result<ChapterManageDto>> ReadAsync(IPanelForgeDbContext db, Guid chapterId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var chapter = await db.Chapters.AsNoTracking()
            .Where(c => c.Id == chapterId)
            .Select(c => new
            {
                c.Id, c.SeriesId, c.ChapterNumber, c.Title, c.IsLocked, c.PriceVnd, c.PreviewPagesCustomized,
                IsPublished = c.Status == ChapterStatus.Published && c.PublishedAt <= now,
                c.Series.DefaultPreviewPageCount
            })
            .FirstOrDefaultAsync(ct);
        if (chapter is null)
            return Result<ChapterManageDto>.Failure("Không tìm thấy chương.", ResultErrorCodes.NotFound);

        var previews = await ChapterReadAccess.ResolvePreviewPagesAsync(db, chapterId, ct);
        var withArtwork = (await db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId && db.AssetReferences.Any(r => r.PageId == p.Id))
            .Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var translations = await db.PageTranslations.AsNoTracking()
            .Where(t => t.ChapterId == chapterId && !t.IsDeleted)
            .Select(t => new { t.PageId, t.Language })
            .ToListAsync(ct);
        var customPrices = await db.ChapterLanguagePrices.AsNoTracking()
            .Where(p => p.ChapterId == chapterId && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Language, p => p.PriceVnd, ct);

        var pageCount = previews.Count;
        var languages = ContentLanguages.All.Select(lang =>
        {
            var translated = lang == ContentLanguages.Original
                ? pageCount
                : translations.Where(t => t.Language == lang).Select(t => t.PageId).Distinct().Count();
            return new ChapterLanguageStatusDto(lang, translated,
                lang == ContentLanguages.Original || (pageCount > 0 && translated >= pageCount));
        }).ToList();

        var pages = previews.Select(p =>
        {
            var urls = ContentLanguages.All.ToDictionary(
                lang => lang,
                lang => (lang == ContentLanguages.Original
                    ? withArtwork.Contains(p.PageId)
                    : translations.Any(t => t.PageId == p.PageId && t.Language == lang))
                    ? PreviewImageUrl(chapterId, p.PageId, lang)
                    : null);
            return new ManagedPageDto(p.PageId, p.PageNumber, p.IsPreview, urls);
        }).ToList();

        return Result<ChapterManageDto>.Success(new ChapterManageDto(
            chapter.Id, chapter.SeriesId, chapter.ChapterNumber, chapter.Title, chapter.IsPublished,
            chapter.IsLocked, chapter.PriceVnd, chapter.PreviewPagesCustomized,
            chapter.DefaultPreviewPageCount,
            ContentLanguages.All.Select(l => customPrices.TryGetValue(l, out var price)
                ? new ChapterLanguagePriceDto(l, price, true)
                : new ChapterLanguagePriceDto(l, chapter.PriceVnd, false)).ToList(),
            languages,
            pages));
    }
}

// ─── Command: Publisher chọn trang xem trước ─────────────────────────────────

/// <summary>
/// Publisher chọn các trang xem trước của chương (người chưa mua chương trả phí xem được các trang này, trang khác khóa).
/// Chương chuyển sang "chọn tay": không theo mặc định "N trang đầu" của Series nữa.
/// </summary>
public sealed record SetChapterPreviewPagesCommand(Guid ChapterId, IReadOnlyList<Guid> PreviewPageIds)
    : IRequest<Result<ChapterManageDto>>;

public sealed class SetChapterPreviewPagesCommandHandler
    : IRequestHandler<SetChapterPreviewPagesCommand, Result<ChapterManageDto>>
{
    private readonly IPanelForgeDbContext _db;

    public SetChapterPreviewPagesCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterManageDto>> Handle(SetChapterPreviewPagesCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<ChapterManageDto>.Failure("Không tìm thấy chương.", ResultErrorCodes.NotFound);

        var previewIds = cmd.PreviewPageIds.ToHashSet();
        var pages = await _db.Pages.Where(p => p.ChapterId == chapter.Id).ToListAsync(ct);
        if (previewIds.Any(id => pages.All(p => p.Id != id)))
            return Result<ChapterManageDto>.Failure("Có trang không thuộc chương này.", ResultErrorCodes.NotFound);

        foreach (var page in pages) page.SetPreview(previewIds.Contains(page.Id));
        chapter.MarkPreviewPagesCustomized();

        await _db.SaveChangesAsync(ct);
        return await ChapterManageReader.ReadAsync(_db, chapter.Id, ct);
    }
}

/// <summary>Bỏ chọn tay: chương quay về theo mặc định "N trang đầu xem trước" của Series.</summary>
public sealed record ResetChapterPreviewPagesCommand(Guid ChapterId) : IRequest<Result<ChapterManageDto>>;

public sealed class ResetChapterPreviewPagesCommandHandler
    : IRequestHandler<ResetChapterPreviewPagesCommand, Result<ChapterManageDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ResetChapterPreviewPagesCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterManageDto>> Handle(ResetChapterPreviewPagesCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<ChapterManageDto>.Failure("Không tìm thấy chương.", ResultErrorCodes.NotFound);

        chapter.MarkPreviewPagesCustomized(false);
        await _db.SaveChangesAsync(ct);
        return await ChapterManageReader.ReadAsync(_db, chapter.Id, ct);
    }
}

// ─── Command: giá chương theo ngôn ngữ ───────────────────────────────────────

/// <summary>Publisher đặt giá mở khóa chương trả phí riêng cho từng ngôn ngữ (bỏ trống = giá chung).</summary>
public sealed record SetChapterLanguagePricesCommand(Guid ChapterId, IReadOnlyList<LanguagePriceItem> Prices)
    : IRequest<Result<ChapterManageDto>>;

public sealed class SetChapterLanguagePricesCommandHandler
    : IRequestHandler<SetChapterLanguagePricesCommand, Result<ChapterManageDto>>
{
    public const decimal MinChapterPriceVnd = 1000m;
    public const decimal MaxChapterPriceVnd = 1_000_000m;

    private readonly IPanelForgeDbContext _db;

    public SetChapterLanguagePricesCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterManageDto>> Handle(SetChapterLanguagePricesCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == cmd.ChapterId)
            .Select(c => new { c.Id, c.IsLocked })
            .FirstOrDefaultAsync(ct);
        if (chapter is null)
            return Result<ChapterManageDto>.Failure("Không tìm thấy chương.", ResultErrorCodes.NotFound);
        if (!chapter.IsLocked)
            return Result<ChapterManageDto>.Failure(
                "Chương miễn phí không có giá mở khóa. Giá theo ngôn ngữ chỉ áp dụng cho chương trả phí.",
                ResultErrorCodes.Validation);

        var items = new List<(string Language, decimal? Price)>();
        foreach (var item in cmd.Prices)
        {
            var lang = ContentLanguages.Normalize(item.Language);
            if (lang is null || string.IsNullOrWhiteSpace(item.Language))
                return Result<ChapterManageDto>.Failure($"Ngôn ngữ '{item.Language}' không được hỗ trợ.", ResultErrorCodes.Validation);
            var price = item.PriceVnd is > 0 ? item.PriceVnd : null;
            if (price is < MinChapterPriceVnd or > MaxChapterPriceVnd)
                return Result<ChapterManageDto>.Failure(
                    $"Giá chương phải từ {MinChapterPriceVnd:N0}đ đến {MaxChapterPriceVnd:N0}đ.", ResultErrorCodes.Validation);
            items.Add((lang, price));
        }
        if (items.Select(i => i.Language).Distinct().Count() != items.Count)
            return Result<ChapterManageDto>.Failure("Ngôn ngữ bị trùng.", ResultErrorCodes.Validation);

        var existing = await _db.ChapterLanguagePrices.Where(p => p.ChapterId == chapter.Id).ToListAsync(ct);
        foreach (var (lang, price) in items)
        {
            var row = existing.FirstOrDefault(p => p.Language == lang);
            if (price is null)
            {
                if (row is not null) _db.ChapterLanguagePrices.Remove(row);
            }
            else if (row is null) _db.ChapterLanguagePrices.Add(ChapterLanguagePrice.Create(chapter.Id, lang, price.Value));
            else row.SetPrice(price.Value);
        }

        await _db.SaveChangesAsync(ct);
        return await ChapterManageReader.ReadAsync(_db, chapter.Id, ct);
    }
}

// ─── Command: ảnh trang bản dịch ─────────────────────────────────────────────

/// <summary>
/// Publisher upload ảnh trang đã dịch (en/ja; PNG/JPEG/WebP ≤ 15 MB) hoặc xóa (<see cref="Content"/> = null).
/// Bản ngôn ngữ phát hành cho Reader khi mọi trang của chương có ảnh dịch.
/// </summary>
public sealed record SetPageTranslationCommand(Guid ChapterId, Guid PageId, string Language, byte[]? Content, Guid UserId)
    : IRequest<Result<ChapterManageDto>>;

public sealed class SetPageTranslationCommandHandler : IRequestHandler<SetPageTranslationCommand, Result<ChapterManageDto>>
{
    public const long MaxBytes = 15 * 1024 * 1024;

    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public SetPageTranslationCommandHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<Result<ChapterManageDto>> Handle(SetPageTranslationCommand cmd, CancellationToken ct)
    {
        var language = ContentLanguages.Normalize(cmd.Language);
        if (language is null || !ContentLanguages.Translations.Contains(language))
            return Failure("Chỉ upload ảnh cho bản dịch (en, ja); bản tiếng Việt dùng ảnh Studio.", ResultErrorCodes.Validation);

        var pageExists = await _db.Pages.AsNoTracking().AnyAsync(p => p.Id == cmd.PageId && p.ChapterId == cmd.ChapterId, ct);
        if (!pageExists)
            return Failure("Trang không thuộc chương này.", ResultErrorCodes.NotFound);

        var existing = await _db.PageTranslations
            .FirstOrDefaultAsync(t => t.PageId == cmd.PageId && t.Language == language, ct);

        if (cmd.Content is null)
        {
            if (existing is not null) _db.PageTranslations.Remove(existing);
        }
        else
        {
            if (cmd.Content.Length == 0 || cmd.Content.Length > MaxBytes)
                return Failure("Ảnh trang phải từ 1 byte đến 15 MB.", ResultErrorCodes.Validation);
            var contentType = AssetRules.DetectImageContentType(cmd.Content);
            if (contentType is null)
                return Failure("Chỉ nhận ảnh PNG, JPEG hoặc WebP.", ResultErrorCodes.Validation);

            var sha = AssetRules.ComputeSha256(cmd.Content);
            await _storage.SaveAsync(PageTranslation.StorageKey(cmd.PageId, language, sha), cmd.Content,
                new AssetObjectInfo(contentType, new Dictionary<string, string> { ["kind"] = "page-translation", ["lang"] = language }), ct);

            if (existing is null)
            {
                var row = PageTranslation.Create(cmd.PageId, cmd.ChapterId, language, sha, contentType);
                row.CreatedBy = cmd.UserId.ToString();
                _db.PageTranslations.Add(row);
            }
            else
            {
                existing.Replace(sha, contentType);
                existing.UpdatedBy = cmd.UserId.ToString();
            }
        }

        await _db.SaveChangesAsync(ct);
        return await ChapterManageReader.ReadAsync(_db, cmd.ChapterId, ct);
    }

    private static Result<ChapterManageDto> Failure(string message, string code) =>
        Result<ChapterManageDto>.Failure(message, code);
}

// ─── Query: ảnh xem trước cho Publisher (không cần chương đã phát hành) ──────

public sealed record GetPublisherPageImageQuery(Guid ChapterId, Guid PageId, string? Language)
    : IRequest<Result<Assets.Models.AssetDownloadDto>>;

public sealed class GetPublisherPageImageQueryHandler
    : IRequestHandler<GetPublisherPageImageQuery, Result<Assets.Models.AssetDownloadDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;
    private readonly IPageRepository _pages;
    private readonly IPageLetteringCompositor _compositor;

    public GetPublisherPageImageQueryHandler(
        IPanelForgeDbContext db, IAssetStorage storage, IPageRepository pages, IPageLetteringCompositor compositor)
    {
        _db = db;
        _storage = storage;
        _pages = pages;
        _compositor = compositor;
    }

    public async Task<Result<Assets.Models.AssetDownloadDto>> Handle(GetPublisherPageImageQuery q, CancellationToken ct)
    {
        var language = ContentLanguages.Normalize(q.Language);
        var image = language is null
            ? null
            : await GetPublicPageImageQueryHandler.LoadImageAsync(_db, _storage, _pages, _compositor, q.ChapterId, q.PageId, language, ct);
        return image is null
            ? Result<Assets.Models.AssetDownloadDto>.Failure("Trang chưa có ảnh cho ngôn ngữ này.", ResultErrorCodes.NotFound)
            : Result<Assets.Models.AssetDownloadDto>.Success(image);
    }
}
