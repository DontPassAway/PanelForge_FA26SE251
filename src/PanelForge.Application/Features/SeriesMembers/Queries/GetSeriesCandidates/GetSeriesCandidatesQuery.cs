using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.SeriesMembers.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.SeriesMembers.Queries.GetSeriesCandidates;

/// <summary>
/// GET /api/series/{seriesId}/members/candidates
/// Lấy danh sách thành viên trong Studio Workspace sẵn sàng để thêm vào Series.
/// </summary>
public sealed record GetSeriesCandidatesQuery(Guid UserId, Guid SeriesId)
    : IRequest<Result<IReadOnlyList<SeriesCandidateDto>>>;

public sealed class GetSeriesCandidatesQueryHandler
    : IRequestHandler<GetSeriesCandidatesQuery, Result<IReadOnlyList<SeriesCandidateDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public GetSeriesCandidatesQueryHandler(
        IPanelForgeDbContext dbContext,
        IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<IReadOnlyList<SeriesCandidateDto>>> Handle(
        GetSeriesCandidatesQuery query,
        CancellationToken cancellationToken)
    {
        var series = await _dbContext.Series
            .Where(s => s.Id == query.SeriesId)
            .Select(s => new { s.Id, s.WorkspaceId, OwnerId = s.Workspace.OwnerId })
            .FirstOrDefaultAsync(cancellationToken);

        if (series is null)
            return Result<IReadOnlyList<SeriesCandidateDto>>.Failure("Series không tồn tại.", ResultErrorCodes.NotFound);

        // Chỉ Producer hoặc Owner của Workspace mới có quyền lấy danh sách ứng viên để gán vào Series
        if (!await _authorizationService.IsOwnerOrProducerAsync(query.UserId, series.WorkspaceId, cancellationToken))
            return Result<IReadOnlyList<SeriesCandidateDto>>.Failure(
                "Chỉ Producer hoặc Owner của Studio Workspace mới có quyền quản lý thành viên Series.",
                ResultErrorCodes.Forbidden);

        // 1. Lấy tất cả thành viên Active của Workspace kèm thông tin User
        var workspaceMembers = await _dbContext.WorkspaceMembers
            .Include(wm => wm.User)
            .Where(wm => wm.WorkspaceId == series.WorkspaceId && wm.Status == WorkspaceMemberStatus.Active)
            .OrderBy(wm => wm.User.FullName)
            .ToListAsync(cancellationToken);

        // 2. Lấy thông tin các thành viên đã có trong Series này
        var seriesMembersMap = await _dbContext.SeriesMembers
            .Where(sm => sm.SeriesId == query.SeriesId)
            .ToDictionaryAsync(sm => sm.UserId, sm => sm.Role.ToString(), cancellationToken);

        // 3. Ghép danh sách ứng viên (Producer và Owner luôn mặc định là thành viên của mọi Series)
        IReadOnlyList<SeriesCandidateDto> candidates = workspaceMembers
            .Select(wm =>
            {
                var isProducerOrOwner = wm.Role == WorkspaceRole.Producer || wm.UserId == series.OwnerId;
                var isAlreadyMember = isProducerOrOwner || seriesMembersMap.ContainsKey(wm.UserId);
                var currentRole = seriesMembersMap.GetValueOrDefault(wm.UserId) ?? (isProducerOrOwner ? "Producer" : null);

                return new SeriesCandidateDto(
                    wm.UserId,
                    wm.User.FullName,
                    wm.User.Email,
                    wm.User.AvatarUrl,
                    wm.Role.ToString(),
                    isAlreadyMember,
                    currentRole
                );
            })
            .ToList();

        return Result<IReadOnlyList<SeriesCandidateDto>>.Success(candidates);
    }
}
