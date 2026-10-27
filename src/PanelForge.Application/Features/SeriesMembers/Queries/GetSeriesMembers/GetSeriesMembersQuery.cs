using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.SeriesMembers.Models;

namespace PanelForge.Application.Features.SeriesMembers.Queries.GetSeriesMembers;

/// <summary>GET /api/series/{seriesId}/members — Danh sách thành viên được phân công vào Series.</summary>
public sealed record GetSeriesMembersQuery(Guid UserId, Guid SeriesId)
    : IRequest<Result<IReadOnlyList<SeriesMemberDto>>>;

public sealed class GetSeriesMembersQueryHandler
    : IRequestHandler<GetSeriesMembersQuery, Result<IReadOnlyList<SeriesMemberDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public GetSeriesMembersQueryHandler(
        IPanelForgeDbContext dbContext,
        IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<IReadOnlyList<SeriesMemberDto>>> Handle(
        GetSeriesMembersQuery query,
        CancellationToken cancellationToken)
    {
        var series = await _dbContext.Series
            .Where(s => s.Id == query.SeriesId)
            .Select(s => new { s.Id, s.WorkspaceId, OwnerId = s.Workspace.OwnerId })
            .FirstOrDefaultAsync(cancellationToken);

        if (series is null)
            return Result<IReadOnlyList<SeriesMemberDto>>.Failure("Series không tồn tại.", ResultErrorCodes.NotFound);

        // Người dùng phải là thành viên của Series (hoặc Producer / Owner của Workspace) để xem danh sách thành viên Series
        if (!await _authorizationService.IsSeriesMemberAsync(query.UserId, query.SeriesId, cancellationToken))
            return Result<IReadOnlyList<SeriesMemberDto>>.Failure(
                "Bạn phải là thành viên được phân công vào dự án Series hoặc quản trị viên để xem danh sách thành viên.",
                ResultErrorCodes.Forbidden);

        var members = await _dbContext.SeriesMembers
            .Include(sm => sm.User)
            .Where(sm => sm.SeriesId == query.SeriesId)
            .OrderBy(sm => sm.AssignedAt)
            .ToListAsync(cancellationToken);

        // Lấy danh sách Producer hoặc Owner của Workspace (luôn mặc định là thành viên Series)
        var workspaceProducers = await _dbContext.WorkspaceMembers
            .Include(wm => wm.User)
            .Where(wm => wm.WorkspaceId == series.WorkspaceId &&
                         wm.Status == Domain.Enums.WorkspaceMemberStatus.Active &&
                         (wm.Role == Domain.Enums.WorkspaceRole.Producer || wm.UserId == series.OwnerId))
            .ToListAsync(cancellationToken);

        var memberDict = members.ToDictionary(sm => sm.UserId);
        var combinedResult = new List<SeriesMemberDto>();

        // 1. Đưa Producer / Owner lên đầu danh sách
        foreach (var wp in workspaceProducers)
        {
            var explicitAssignment = memberDict.GetValueOrDefault(wp.UserId);
            combinedResult.Add(new SeriesMemberDto(
                explicitAssignment?.Id ?? Guid.Empty,
                query.SeriesId,
                wp.UserId,
                wp.User.FullName,
                wp.User.Email,
                wp.User.AvatarUrl,
                explicitAssignment?.Role ?? Domain.Enums.WorkspaceRole.Producer,
                explicitAssignment?.AssignedAt ?? wp.JoinedAt
            ));
        }

        // 2. Thêm các thành viên được phân công khác
        foreach (var sm in members)
        {
            if (workspaceProducers.Any(wp => wp.UserId == sm.UserId))
                continue;

            combinedResult.Add(new SeriesMemberDto(
                sm.Id,
                sm.SeriesId,
                sm.UserId,
                sm.User.FullName,
                sm.User.Email,
                sm.User.AvatarUrl,
                sm.Role,
                sm.AssignedAt
            ));
        }

        return Result<IReadOnlyList<SeriesMemberDto>>.Success(combinedResult);
    }
}
