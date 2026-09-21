using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Models;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Chapters.Queries.GetChaptersBySeries;

public sealed record GetChaptersBySeriesQuery(
    Guid SeriesId,
    int PageIndex = 1,
    int PageSize = 20
) : IRequest<Result<PagedResult<ChapterDto>>>;

public sealed class GetChaptersBySeriesQueryHandler : IRequestHandler<GetChaptersBySeriesQuery, Result<PagedResult<ChapterDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetChaptersBySeriesQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedResult<ChapterDto>>> Handle(GetChaptersBySeriesQuery query, CancellationToken cancellationToken)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var baseQuery = _dbContext.Chapters
            .AsNoTracking()
            .Where(c => c.SeriesId == query.SeriesId);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var items = await baseQuery
            .OrderBy(c => c.ChapterNumber)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ChapterDto(
                c.Id,
                c.SeriesId,
                c.ChapterNumber,
                c.Title,
                c.TargetReleaseDate,
                c.VersionVector,
                c.CreatedAt,
                c.UpdatedAt,
                c.CreatedBy,
                c.Scenes.Count,
                c.Pages.Count
            ))
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var pagedResult = new PagedResult<ChapterDto>(
            Items: items,
            TotalCount: totalCount,
            PageIndex: pageIndex,
            PageSize: pageSize,
            TotalPages: totalPages
        );

        return Result<PagedResult<ChapterDto>>.Success(pagedResult);
    }
}
