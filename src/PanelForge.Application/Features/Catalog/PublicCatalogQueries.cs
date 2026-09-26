using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Models;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Catalog;

// BR-23 / BR-24: public catalog of Published Series, độc lập với workspace membership.
// Chỉ lộ thông tin đã công bố: Series có ít nhất một Chapter Published và danh sách các Chapter Published đó.
// Không bao giờ trả Series Bible, pipeline, thành viên hay Chapter chưa Published.

public sealed record PublicSeriesDto(
    Guid Id,
    string Title,
    string? Synopsis,
    string Genre,
    string Format,
    ReadingDirection ReadingDirection,
    string StudioName,
    int PublishedChapterCount,
    DateTime? LatestPublishedAt);

public sealed record PublicChapterDto(Guid Id, decimal ChapterNumber, string? Title, DateTime? PublishedAt);

public sealed record PublicSeriesDetailDto(
    Guid Id,
    string Title,
    string? Synopsis,
    string Genre,
    string Format,
    ReadingDirection ReadingDirection,
    string StudioName,
    IReadOnlyList<PublicChapterDto> Chapters);

// ─── List ─────────────────────────────────────────────────────────────────────

/// <param name="Search">Tìm theo tên Series (không phân biệt hoa thường).</param>
/// <param name="Genre">Lọc chính xác theo thể loại.</param>
public sealed record GetPublicCatalogQuery(string? Search = null, string? Genre = null, int PageIndex = 1, int PageSize = 20)
    : IRequest<Result<PagedResult<PublicSeriesDto>>>;

public sealed class GetPublicCatalogQueryHandler : IRequestHandler<GetPublicCatalogQuery, Result<PagedResult<PublicSeriesDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetPublicCatalogQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<PagedResult<PublicSeriesDto>>> Handle(GetPublicCatalogQuery q, CancellationToken ct)
    {
        var pageIndex = Math.Max(1, q.PageIndex);
        var pageSize = Math.Clamp(q.PageSize, 1, 50);

        var query = _dbContext.Series.AsNoTracking()
            .Where(s => s.Chapters.Any(c => c.Status == ChapterStatus.Published));

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim().ToLower();
            query = query.Where(s => s.Title.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(q.Genre))
        {
            var genre = q.Genre.Trim();
            query = query.Where(s => s.Genre == genre);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .Select(s => new PublicSeriesDto(
                s.Id,
                s.Title,
                s.Synopsis,
                s.Genre,
                s.Format,
                s.ReadingDirection,
                s.Workspace.Name,
                s.Chapters.Count(c => c.Status == ChapterStatus.Published),
                s.Chapters.Where(c => c.Status == ChapterStatus.Published).Max(c => c.PublishedAt)))
            .OrderByDescending(s => s.LatestPublishedAt)
            .ThenBy(s => s.Title)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Result<PagedResult<PublicSeriesDto>>.Success(new PagedResult<PublicSeriesDto>(
            items, total, pageIndex, pageSize, (int)Math.Ceiling(total / (double)pageSize)));
    }
}

// ─── Detail ───────────────────────────────────────────────────────────────────

public sealed record GetPublicSeriesDetailQuery(Guid SeriesId) : IRequest<Result<PublicSeriesDetailDto>>;

public sealed class GetPublicSeriesDetailQueryHandler : IRequestHandler<GetPublicSeriesDetailQuery, Result<PublicSeriesDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetPublicSeriesDetailQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<PublicSeriesDetailDto>> Handle(GetPublicSeriesDetailQuery q, CancellationToken ct)
    {
        var series = await _dbContext.Series.AsNoTracking()
            .Where(s => s.Id == q.SeriesId)
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.Synopsis,
                s.Genre,
                s.Format,
                s.ReadingDirection,
                StudioName = s.Workspace.Name,
                Chapters = s.Chapters
                    .Where(c => c.Status == ChapterStatus.Published)
                    .OrderBy(c => c.ChapterNumber)
                    .Select(c => new PublicChapterDto(c.Id, c.ChapterNumber, c.Title, c.PublishedAt))
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        // Series chưa có chương Published coi như không tồn tại với public (không lộ Series đang sản xuất)
        if (series is null || series.Chapters.Count == 0)
            return Result<PublicSeriesDetailDto>.Failure("Không tìm thấy Series đã phát hành.", ResultErrorCodes.NotFound);

        return Result<PublicSeriesDetailDto>.Success(new PublicSeriesDetailDto(
            series.Id, series.Title, series.Synopsis, series.Genre, series.Format, series.ReadingDirection,
            series.StudioName, series.Chapters));
    }
}
