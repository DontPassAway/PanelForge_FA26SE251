using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Workspaces.Commands.DeleteWorkspace;

/// <summary>DELETE /api/workspaces/{id} — chỉ Owner. DbContext chuyển Remove thành soft-delete.</summary>
public sealed record DeleteWorkspaceCommand(Guid UserId, Guid WorkspaceId) : IRequest<Result<bool>>;

public sealed class DeleteWorkspaceCommandHandler : IRequestHandler<DeleteWorkspaceCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public DeleteWorkspaceCommandHandler(IPanelForgeDbContext dbContext, IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<bool>> Handle(DeleteWorkspaceCommand command, CancellationToken cancellationToken)
    {
        if (!await _authorizationService.IsOwnerAsync(command.UserId, command.WorkspaceId, cancellationToken))
            return Result<bool>.Failure("Chỉ Chủ sở hữu (Owner) mới có quyền xóa Workspace.", ResultErrorCodes.Forbidden);

        var workspace = await _dbContext.StudioWorkspaces
            .Include(w => w.Members)
            .FirstOrDefaultAsync(w => w.Id == command.WorkspaceId, cancellationToken);
        if (workspace is null)
            return Result<bool>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        _dbContext.WorkspaceMembers.RemoveRange(workspace.Members);
        _dbContext.StudioWorkspaces.Remove(workspace);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
