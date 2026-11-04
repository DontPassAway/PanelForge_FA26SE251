using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Models;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Chapters.Commands.SetChapterCover;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Series.Queries.GetSeriesCover;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Catalog;

// Họp tuần 5: Reader đọc nội dung theo lịch phát hành. Chỉ chương Published đã đến giờ (PublishedAt <= now) mới đọc được;
// ngoài ra trả 404 như chương không tồn tại để không lộ nội dung đang sản xuất hay đã lên lịch.

/// <param name="ImageUrl">Đường dẫn tương đối tới ảnh trang; null nếu trang chưa có artwork.</param>
public sealed record PublicPageDto(Guid Id, int PageNumber, string? ImageUrl);

public sealed record PublicChapterReadDto(
    Guid Id,
    Guid SeriesId,
    string SeriesTitle,
    ReadingDirection ReadingDirection,
    decimal ChapterNumber,
    string? Title,
    DateTime? PublishedAt,
    IReadOnlyList<PublicPageDto> Pages,
    string? CoverUrl = null);

// ─── Chapter + danh sách trang ────────────────────────────────────────────────

public sealed record GetPublicChapterQuery(Guid ChapterId) : IRequest<Result<PublicChapterReadDto>>;

public sealed class GetPublicChapterQueryHandler : IRequestHandler<GetPublicChapterQuery, Result<PublicChapterReadDto>>
{
    private readonly IPanelForgeDbContext _db;
    public GetPublicChapterQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<PublicChapterReadDto>> Handle(GetPublicChapterQuery q, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var chapter = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == q.ChapterId && c.Status == ChapterStatus.Published && c.PublishedAt <= now)
            .Select(c => new
            {
                c.Id, c.SeriesId, SeriesTitle = c.Series.Title, c.Series.ReadingDirection,
                c.ChapterNumber, c.Title, c.PublishedAt, c.CoverImageSha256
            })
            .FirstOrDefaultAsync(ct);
        if (chapter is null) return NotFound();

        var pages = await _db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == q.ChapterId)
            .OrderBy(p => p.PageNumber)
            .Select(p => new
            {
                p.Id, p.PageNumber,
                HasArtwork = _db.AssetReferences.Any(r => r.PageId == p.Id)
            })
            .ToListAsync(ct);

        var dto = new PublicChapterReadDto(
            chapter.Id, chapter.SeriesId, chapter.SeriesTitle, chapter.ReadingDirection, chapter.ChapterNumber,
            chapter.Title, chapter.PublishedAt,
            pages.Select(p => new PublicPageDto(
                p.Id, p.PageNumber,
                p.HasArtwork ? $"/api/catalog/chapters/{chapter.Id}/pages/{p.Id}/image" : null)).ToList(),
            GetPublicChapterCoverQueryHandler.CoverUrl(chapter.Id, chapter.CoverImageSha256));
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

// ─── Ảnh một trang ────────────────────────────────────────────────────────────

/// <summary>
/// Ảnh Reader thấy = trang hoàn chỉnh theo <see cref="FinalArtwork"/> (ảnh cả trang, hoặc ghép ảnh từng Panel vào khung)
/// kèm lớp chữ — cùng kết quả với xem thử và xuất file. Chương chỉ Published sau khi mọi việc Approved.
/// </summary>
public sealed record GetPublicPageImageQuery(Guid ChapterId, Guid PageId) : IRequest<Result<AssetDownloadDto>>;

public sealed class GetPublicPageImageQueryHandler : IRequestHandler<GetPublicPageImageQuery, Result<AssetDownloadDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;
    private readonly IPageRepository _pages;
    private readonly IPageLetteringCompositor _compositor;

    public GetPublicPageImageQueryHandler(
        IPanelForgeDbContext db, IAssetStorage storage, IPageRepository pages, IPageLetteringCompositor compositor)
    {
        _db = db;
        _storage = storage;
        _pages = pages;
        _compositor = compositor;
    }

    public async Task<Result<AssetDownloadDto>> Handle(GetPublicPageImageQuery q, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var visible = await _db.Chapters.AsNoTracking()
            .AnyAsync(c => c.Id == q.ChapterId && c.Status == ChapterStatus.Published && c.PublishedAt <= now, ct);
        if (!visible) return NotFound();

        var artwork = (await FinalArtwork.SelectAsync(_db, q.ChapterId, ct, q.PageId)).FirstOrDefault();
        if (artwork is null) return NotFound();

        var image = await PageImageBuilder.BuildAsync(
            _db, _storage, _pages, _compositor, q.PageId, artwork.AssetReferenceIds.ToList(), ct);
        if (image is null) return NotFound();

        return Result<AssetDownloadDto>.Success(new AssetDownloadDto(
            new MemoryStream(image.Content), image.ContentType, $"trang-{image.PageNumber}.png"));
    }

    private static Result<AssetDownloadDto> NotFound() =>
        Result<AssetDownloadDto>.Failure("Không tìm thấy ảnh trang đã phát hành.", ResultErrorCodes.NotFound);
}
