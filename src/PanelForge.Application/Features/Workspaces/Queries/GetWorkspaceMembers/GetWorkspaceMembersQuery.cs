using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceMembers;

/// <summary>GET /api/workspaces/{id}/members — thành viên của Studio, theo thứ tự tham gia.</summary>
public sealed record GetWorkspaceMembersQuery(Guid UserId, Guid WorkspaceId)
    : IRequest<Result<IReadOnlyList<WorkspaceMemberDto>>>;

public sealed class GetWorkspaceMembersQueryHandler
    : IRequestHandler<GetWorkspaceMembersQuery, Result<IReadOnlyList<WorkspaceMemberDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public GetWorkspaceMembersQueryHandler(IPanelForgeDbContext dbContext, IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<IReadOnlyList<WorkspaceMemberDto>>> Handle(
        GetWorkspaceMembersQuery query, CancellationToken cancellationToken)
    {
        if (!await _authorizationService.IsMemberAsync(query.UserId, query.WorkspaceId, cancellationToken))
            return Result<IReadOnlyList<WorkspaceMemberDto>>.Failure(
                "Bạn phải là thành viên của Workspace để xem danh sách thành viên.", ResultErrorCodes.Forbidden);

        var ownerId = await _dbContext.StudioWorkspaces
            .Where(w => w.Id == query.WorkspaceId)
            .Select(w => (Guid?)w.OwnerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (ownerId is null)
            return Result<IReadOnlyList<WorkspaceMemberDto>>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        var members = await _dbContext.WorkspaceMembers
            .Include(m => m.User)
            .Where(m => m.WorkspaceId == query.WorkspaceId)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<WorkspaceMemberDto> result = members
            .Select(m => WorkspaceMapper.ToMemberDto(m, m.User, ownerId.Value))
            .ToList();
        return Result<IReadOnlyList<WorkspaceMemberDto>>.Success(result);
    }
}
