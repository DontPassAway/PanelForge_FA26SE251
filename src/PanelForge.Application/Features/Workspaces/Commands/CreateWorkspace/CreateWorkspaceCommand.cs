using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Commands.CreateWorkspace;

/// <summary>
/// POST /api/workspaces — tạo Studio. Chỉ User có CanCreateStudio (BR-22), không phải Admin (BR-07).
/// Người tạo tự động là Producer trong workspace_members.
/// </summary>
public sealed record CreateWorkspaceCommand(Guid OwnerId, string Name, long StorageQuotaBytes)
    : IRequest<Result<WorkspaceDto>>;

public sealed class CreateWorkspaceCommandHandler : IRequestHandler<CreateWorkspaceCommand, Result<WorkspaceDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public CreateWorkspaceCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<WorkspaceDto>> Handle(CreateWorkspaceCommand command, CancellationToken cancellationToken)
    {
        var owner = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.OwnerId, cancellationToken);
        if (owner is null || !owner.IsActive)
            return Unauthorized("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.");

        if (owner.Role == SystemRole.Admin)
            return Unauthorized("Administrator không được phép tạo Studio (BR-07).");

        if (!owner.CanCreateStudio)
            return Unauthorized("Bạn không có quyền tạo Studio. Vui lòng yêu cầu Administrator cấp quyền CanCreateStudio.");

        StudioWorkspace workspace;
        try
        {
            workspace = StudioWorkspace.Create(command.Name, owner.Id, command.StorageQuotaBytes);
        }
        catch (ArgumentException ex)
        {
            return Result<WorkspaceDto>.Failure(ex.Message);
        }

        _dbContext.StudioWorkspaces.Add(workspace);
        _dbContext.WorkspaceMembers.Add(WorkspaceMember.Create(workspace.Id, owner.Id, WorkspaceRole.Producer));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<WorkspaceDto>.Success(new WorkspaceDto(
            Id: workspace.Id,
            Name: workspace.Name,
            OwnerId: owner.Id,
            OwnerName: owner.FullName,
            OwnerEmail: owner.Email,
            StorageQuotaBytes: workspace.StorageQuotaBytes,
            UsedStorageBytes: workspace.UsedStorageBytes,
            CreatedAt: workspace.CreatedAt,
            IsOwner: true,
            CurrentUserRole: WorkspaceRole.Producer,
            MemberCount: 1));
    }

    private static Result<WorkspaceDto> Unauthorized(string message)
        => Result<WorkspaceDto>.Failure(message, ResultErrorCodes.Unauthorized);
}
