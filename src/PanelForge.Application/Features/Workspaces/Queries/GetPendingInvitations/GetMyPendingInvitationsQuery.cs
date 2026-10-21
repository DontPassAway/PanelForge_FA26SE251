using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Queries.GetPendingInvitations;

public sealed record GetMyPendingInvitationsQuery(Guid UserId)
    : IRequest<Result<IReadOnlyList<PendingInvitationDto>>>;

public sealed class GetMyPendingInvitationsQueryHandler
    : IRequestHandler<GetMyPendingInvitationsQuery, Result<IReadOnlyList<PendingInvitationDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetMyPendingInvitationsQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<PendingInvitationDto>>> Handle(
        GetMyPendingInvitationsQuery query,
        CancellationToken cancellationToken)
    {
        var pendingMembers = await _dbContext.WorkspaceMembers
            .Include(m => m.Workspace)
            .ThenInclude(w => w.Owner)
            .Where(m => m.UserId == query.UserId
                        && m.Status == WorkspaceMemberStatus.Pending
                        && !m.IsDeleted
                        && !m.Workspace.IsDeleted)
            .OrderByDescending(m => m.JoinedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<PendingInvitationDto> result = pendingMembers
            .Select(m => new PendingInvitationDto(
                WorkspaceId: m.WorkspaceId,
                WorkspaceName: m.Workspace.Name,
                OwnerName: m.Workspace.Owner.FullName,
                OwnerEmail: m.Workspace.Owner.Email,
                Role: m.Role,
                InvitedAt: m.JoinedAt
            ))
            .ToList();

        return Result<IReadOnlyList<PendingInvitationDto>>.Success(result);
    }
}
