using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Commands.AcceptInvitation;

public sealed record AcceptWorkspaceInvitationCommand(Guid UserId, Guid WorkspaceId)
    : IRequest<Result<WorkspaceMemberDto>>;

public sealed class AcceptWorkspaceInvitationCommandHandler
    : IRequestHandler<AcceptWorkspaceInvitationCommand, Result<WorkspaceMemberDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public AcceptWorkspaceInvitationCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<WorkspaceMemberDto>> Handle(
        AcceptWorkspaceInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var workspace = await _dbContext.StudioWorkspaces
            .FirstOrDefaultAsync(w => w.Id == command.WorkspaceId, cancellationToken);
        if (workspace is null)
            return Result<WorkspaceMemberDto>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        var member = await _dbContext.WorkspaceMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.WorkspaceId == command.WorkspaceId && m.UserId == command.UserId, cancellationToken);

        if (member is null)
            return Result<WorkspaceMemberDto>.Failure(
                "Bạn không có lời mời tham gia Workspace này.",
                ResultErrorCodes.NotFound);

        if (member.Status == WorkspaceMemberStatus.Active)
            return Result<WorkspaceMemberDto>.Success(
                WorkspaceMapper.ToMemberDto(member, member.User, workspace.OwnerId));

        member.Accept();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<WorkspaceMemberDto>.Success(
            WorkspaceMapper.ToMemberDto(member, member.User, workspace.OwnerId));
    }
}
