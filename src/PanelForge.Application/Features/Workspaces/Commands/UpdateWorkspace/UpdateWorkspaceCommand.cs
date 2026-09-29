using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Workspaces.Commands.UpdateWorkspace;

/// <summary>PUT /api/workspaces/{id} — đổi tên Studio (Owner hoặc Producer).</summary>
public sealed record UpdateWorkspaceCommand(Guid UserId, Guid WorkspaceId, string Name) : IRequest<Result<WorkspaceDto>>;

public sealed class UpdateWorkspaceCommandHandler : IRequestHandler<UpdateWorkspaceCommand, Result<WorkspaceDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public UpdateWorkspaceCommandHandler(IPanelForgeDbContext dbContext, IWorkspaceAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public async Task<Result<WorkspaceDto>> Handle(UpdateWorkspaceCommand command, CancellationToken cancellationToken)
    {
        if (!await _authorizationService.IsOwnerOrProducerAsync(command.UserId, command.WorkspaceId, cancellationToken))
            return Result<WorkspaceDto>.Failure(
                "Chỉ Chủ sở hữu (Owner) hoặc Quản lý sản xuất (Producer) mới có quyền chỉnh sửa Workspace.",
                ResultErrorCodes.Forbidden);

        var workspace = await _dbContext.StudioWorkspaces
            .Include(w => w.Owner)
            .Include(w => w.Members)
            .FirstOrDefaultAsync(w => w.Id == command.WorkspaceId, cancellationToken);
        if (workspace is null)
            return Result<WorkspaceDto>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        try
        {
            workspace.Rename(command.Name);
        }
        catch (ArgumentException ex)
        {
            return Result<WorkspaceDto>.Failure(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<WorkspaceDto>.Success(
            WorkspaceMapper.ToDto(workspace, command.UserId, workspace.Members.Count));
    }
}
