using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Series.Queries.GetSeriesById;

public sealed record GetSeriesByIdQuery(Guid SeriesId) : IRequest<Result<SeriesDetailDto>>;

public sealed class GetSeriesByIdQueryHandler : IRequestHandler<GetSeriesByIdQuery, Result<SeriesDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetSeriesByIdQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SeriesDetailDto>> Handle(GetSeriesByIdQuery query, CancellationToken cancellationToken)
    {
        var series = await _dbContext.Series
            .AsNoTracking()
            .Include(s => s.Bible)
            .Include(s => s.Chapters)
            .FirstOrDefaultAsync(s => s.Id == query.SeriesId, cancellationToken);

        if (series == null)
            return Result<SeriesDetailDto>.Failure($"Series {query.SeriesId} không tồn tại.");

        var chapters = series.Chapters
            .OrderBy(c => c.ChapterNumber)
            .Select(c => new SeriesChapterSummaryDto(
                c.Id,
                c.ChapterNumber,
                c.Title,
                c.TargetReleaseDate,
                c.VersionVector,
                c.CreatedAt
            ))
            .ToList();

        var detail = new SeriesDetailDto(
            Id: series.Id,
            WorkspaceId: series.WorkspaceId,
            Title: series.Title,
            Synopsis: series.Synopsis,
            ReadingDirection: series.ReadingDirection,
            PipelineDefinitionId: series.PipelineDefinitionId,
            BibleId: series.Bible?.Id,
            Genre: series.Genre,
            Format: series.Format,
            ReleaseScheduleJson: series.ReleaseScheduleJson,
            CreatedAt: series.CreatedAt,
            UpdatedAt: series.UpdatedAt,
            CreatedBy: series.CreatedBy,
            Chapters: chapters
        );

        return Result<SeriesDetailDto>.Success(detail);
    }
}
