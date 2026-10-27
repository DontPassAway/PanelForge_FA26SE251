using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Application.Features.Series.Commands.SetSeriesImage;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Application.Features.Series.Queries.GetSeriesById;

public sealed record GetSeriesByIdQuery(Guid SeriesId, Guid? UserId = null) : IRequest<Result<SeriesDetailDto>>;

public sealed class GetSeriesByIdQueryHandler : IRequestHandler<GetSeriesByIdQuery, Result<SeriesDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public GetSeriesByIdQueryHandler(
        IPanelForgeDbContext dbContext,
        IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<SeriesDetailDto>> Handle(GetSeriesByIdQuery query, CancellationToken cancellationToken)
    {
        if (query.UserId.HasValue && !await _authorizationService.IsSeriesMemberAsync(query.UserId.Value, query.SeriesId, cancellationToken))
            return Result<SeriesDetailDto>.Failure("Bạn không có quyền truy cập bộ truyện này.", ResultErrorCodes.Forbidden);

        var series = await _dbContext.Series
            .AsNoTracking()
            .Include(s => s.Bible)
            .Include(s => s.Chapters)
            .FirstOrDefaultAsync(s => s.Id == query.SeriesId, cancellationToken);

        if (series == null)
            return Result<SeriesDetailDto>.Failure($"Series {query.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

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
            Chapters: chapters,
            StoryArc: series.StoryArc,
            CoverUrl: SetSeriesCoverCommandHandler.CoverUrl(series.Id, series.CoverImageSha256),
            AvatarUrl: SetSeriesImageCommandHandler.ImageUrl(series.Id, SeriesImageKind.Avatar, series.AvatarImageSha256),
            BannerUrl: SetSeriesImageCommandHandler.ImageUrl(series.Id, SeriesImageKind.Banner, series.BannerImageSha256)
        );

        return Result<SeriesDetailDto>.Success(detail);
    }
}
