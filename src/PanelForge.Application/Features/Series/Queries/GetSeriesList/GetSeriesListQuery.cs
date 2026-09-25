using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Series.Queries.GetSeriesList;

public sealed record GetSeriesListQuery(
    Guid WorkspaceId,
    Guid UserId,
    string? SearchTerm = null
) : IRequest<Result<IReadOnlyList<SeriesDto>>>;

public sealed class GetSeriesListQueryHandler : IRequestHandler<GetSeriesListQuery, Result<IReadOnlyList<SeriesDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetSeriesListQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<SeriesDto>>> Handle(GetSeriesListQuery query, CancellationToken cancellationToken)
    {
        var dbQuery = _dbContext.Series
            .AsNoTracking()
            .Where(s => s.WorkspaceId == query.WorkspaceId);

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var search = query.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(s => s.Title.ToLower().Contains(search) || 
                                        (s.Synopsis != null && s.Synopsis.ToLower().Contains(search)));
        }

        var list = await dbQuery
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SeriesDto(
                s.Id,
                s.WorkspaceId,
                s.Title,
                s.Synopsis,
                s.ReadingDirection,
                s.PipelineDefinitionId,
                s.Genre,
                s.Format,
                s.ReleaseScheduleJson,
                s.CreatedAt,
                s.UpdatedAt,
                s.CreatedBy,
                s.Chapters.Count
            ))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SeriesDto>>.Success(list);
    }
}
