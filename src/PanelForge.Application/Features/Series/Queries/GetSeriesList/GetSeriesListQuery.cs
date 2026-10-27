using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Application.Features.Series.Commands.SetSeriesImage;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Application.Features.Series.Queries.GetSeriesList;

public sealed record GetSeriesListQuery(
    Guid WorkspaceId,
    Guid UserId,
    string? SearchTerm = null
) : IRequest<Result<IReadOnlyList<SeriesDto>>>;

public sealed class GetSeriesListQueryHandler : IRequestHandler<GetSeriesListQuery, Result<IReadOnlyList<SeriesDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public GetSeriesListQueryHandler(
        IPanelForgeDbContext dbContext,
        IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<IReadOnlyList<SeriesDto>>> Handle(GetSeriesListQuery query, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra nếu là Owner hoặc Producer của Workspace thì xem được toàn bộ Series trong Workspace
        var isOwnerOrProducer = await _authorizationService.IsOwnerOrProducerAsync(query.UserId, query.WorkspaceId, cancellationToken);

        var dbQuery = _dbContext.Series
            .AsNoTracking()
            .Where(s => s.WorkspaceId == query.WorkspaceId);

        // 2. Nếu là vai trò chuyên môn khác (Writer, Artist, Letterer, Editor), chỉ thấy những Series được phân công (SeriesMember)
        if (!isOwnerOrProducer)
        {
            dbQuery = dbQuery.Where(s => s.Members.Any(sm => sm.UserId == query.UserId && !sm.IsDeleted));
        }

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
                s.Chapters.Count,
                s.StoryArc,
                SetSeriesCoverCommandHandler.CoverUrl(s.Id, s.CoverImageSha256),
                SetSeriesImageCommandHandler.ImageUrl(s.Id, SeriesImageKind.Avatar, s.AvatarImageSha256),
                SetSeriesImageCommandHandler.ImageUrl(s.Id, SeriesImageKind.Banner, s.BannerImageSha256)
            ))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SeriesDto>>.Success(list);
    }
}
