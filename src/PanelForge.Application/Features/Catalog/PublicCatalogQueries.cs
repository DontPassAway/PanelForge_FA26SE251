using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
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
        var now = DateTime.UtcNow; // chương lên lịch (PublishedAt tương lai) chưa lộ ra cho Reader

        var query = _dbContext.Series.AsNoTracking()
            .Where(s => s.Chapters.Any(c => c.Status == ChapterStatus.Published && c.PublishedAt <= now));

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

        // Sắp xếp + phân trang trên entity TRƯỚC khi projection vào positional record:
        // EF Core không dịch được OrderBy theo member của record (LatestPublishedAt) sau khi đã Select.
        var items = await query
            .OrderByDescending(s => s.Chapters.Where(c => c.Status == ChapterStatus.Published && c.PublishedAt <= now).Max(c => c.PublishedAt))
            .ThenBy(s => s.Title)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new PublicSeriesDto(
                s.Id,
                s.Title,
                s.Synopsis,
                s.Genre,
                s.Format,
                s.ReadingDirection,
                s.Workspace.Name,
                s.Chapters.Count(c => c.Status == ChapterStatus.Published && c.PublishedAt <= now),
                s.Chapters.Where(c => c.Status == ChapterStatus.Published && c.PublishedAt <= now).Max(c => c.PublishedAt)))
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
        var now = DateTime.UtcNow;
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
                    .Where(c => c.Status == ChapterStatus.Published && c.PublishedAt <= now)
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
