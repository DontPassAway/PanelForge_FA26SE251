using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Workspaces.Models;

namespace PanelForge.Application.Features.Workspaces.Commands.UpdateWorkspaceSessionPolicy;

/// <summary>Administrator đặt session policy cho Workspace (thay toàn bộ; null = bỏ giới hạn). Áp dụng khi user đăng nhập hoặc làm mới token lần tới.</summary>
public sealed record UpdateWorkspaceSessionPolicyCommand(
    Guid WorkspaceId,
    int? SessionLifetimeMinutes,
    int? IdleTimeoutMinutes,
    int? MaxConcurrentSessions
) : IRequest<Result<WorkspaceSessionPolicyDto>>;

public sealed class UpdateWorkspaceSessionPolicyCommandHandler
    : IRequestHandler<UpdateWorkspaceSessionPolicyCommand, Result<WorkspaceSessionPolicyDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateWorkspaceSessionPolicyCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<WorkspaceSessionPolicyDto>> Handle(UpdateWorkspaceSessionPolicyCommand command, CancellationToken cancellationToken)
    {
        var workspace = await _dbContext.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == command.WorkspaceId, cancellationToken);
        if (workspace is null)
            return Result<WorkspaceSessionPolicyDto>.Failure($"Workspace {command.WorkspaceId} không tồn tại.", ResultErrorCodes.NotFound);

        try
        {
            workspace.SetSessionPolicy(command.SessionLifetimeMinutes, command.IdleTimeoutMinutes, command.MaxConcurrentSessions);
        }
        catch (ArgumentException ex)
        {
            return Result<WorkspaceSessionPolicyDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<WorkspaceSessionPolicyDto>.Success(new WorkspaceSessionPolicyDto(
            workspace.Id, workspace.SessionLifetimeMinutes, workspace.SessionIdleTimeoutMinutes, workspace.MaxConcurrentSessions));
    }
}
