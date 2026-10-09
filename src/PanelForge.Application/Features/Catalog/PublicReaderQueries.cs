using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Models;
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
    IReadOnlyList<PublicPageDto> Pages);

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
                c.ChapterNumber, c.Title, c.PublishedAt
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
                p.HasArtwork ? $"/api/catalog/chapters/{chapter.Id}/pages/{p.Id}/image" : null)).ToList());
        return Result<PublicChapterReadDto>.Success(dto);
    }

    private static Result<PublicChapterReadDto> NotFound() =>
        Result<PublicChapterReadDto>.Failure("Không tìm thấy chương đã phát hành.", ResultErrorCodes.NotFound);
}

// ─── Ảnh một trang ────────────────────────────────────────────────────────────

/// <summary>
/// Ảnh Reader thấy = artwork của Page ở stage có <c>StageOrder</c> cao nhất, bản <c>VersionNumber</c> mới nhất
/// (stage cuối đã qua cổng duyệt, vì chương chỉ Published sau khi mọi việc Approved).
/// </summary>
public sealed record GetPublicPageImageQuery(Guid ChapterId, Guid PageId) : IRequest<Result<AssetDownloadDto>>;

public sealed class GetPublicPageImageQueryHandler : IRequestHandler<GetPublicPageImageQuery, Result<AssetDownloadDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;

    public GetPublicPageImageQueryHandler(IPanelForgeDbContext db, IAssetStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<Result<AssetDownloadDto>> Handle(GetPublicPageImageQuery q, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var visible = await _db.Chapters.AsNoTracking()
            .AnyAsync(c => c.Id == q.ChapterId && c.Status == ChapterStatus.Published && c.PublishedAt <= now, ct);
        if (!visible) return NotFound();

        var reference = await (
            from r in _db.AssetReferences.AsNoTracking()
            where r.ChapterId == q.ChapterId && r.PageId == q.PageId
            join s in _db.PipelineStages.AsNoTracking() on r.PipelineStageId equals s.Id
            orderby s.StageOrder descending, r.VersionNumber descending
            select r).FirstOrDefaultAsync(ct);
        if (reference is null) return NotFound();

        var asset = await _db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == reference.AssetId, ct);
        var stream = asset is null ? null : await _storage.OpenReadAsync(asset.StorageKey, ct);
        if (asset is null || stream is null) return NotFound();

        return Result<AssetDownloadDto>.Success(new AssetDownloadDto(stream, asset.ContentType, reference.FileName));
    }

    private static Result<AssetDownloadDto> NotFound() =>
        Result<AssetDownloadDto>.Failure("Không tìm thấy ảnh trang đã phát hành.", ResultErrorCodes.NotFound);
}
