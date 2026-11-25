using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Commands.RejectInvitation;

public sealed record RejectWorkspaceInvitationCommand(Guid UserId, Guid WorkspaceId)
    : IRequest<Result<bool>>;

public sealed class RejectWorkspaceInvitationCommandHandler
    : IRequestHandler<RejectWorkspaceInvitationCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public RejectWorkspaceInvitationCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(
        RejectWorkspaceInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var member = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == command.WorkspaceId && m.UserId == command.UserId, cancellationToken);

        if (member is null)
            return Result<bool>.Failure("Bạn không có lời mời tham gia Workspace này.", ResultErrorCodes.NotFound);

        if (member.Status == WorkspaceMemberStatus.Active)
            return Result<bool>.Failure("Bạn đã là thành viên chính thức, không thể từ chối lời mời.");

        _dbContext.WorkspaceMembers.Remove(member);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
