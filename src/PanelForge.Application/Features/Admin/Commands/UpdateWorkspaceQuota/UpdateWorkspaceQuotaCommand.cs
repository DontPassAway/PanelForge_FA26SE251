using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Admin.Commands.UpdateWorkspaceQuota;

/// <summary>
/// PUT /api/admin/workspaces/{id}/quota — Cập nhật dung lượng lưu trữ tối đa (Storage Quota) cho Studio Workspace (BR-16).
/// </summary>
public sealed record UpdateWorkspaceQuotaCommand(Guid WorkspaceId, long StorageQuotaBytes) : IRequest<Result<AdminWorkspaceDto>>;

public sealed class UpdateWorkspaceQuotaCommandHandler
    : IRequestHandler<UpdateWorkspaceQuotaCommand, Result<AdminWorkspaceDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateWorkspaceQuotaCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<AdminWorkspaceDto>> Handle(
        UpdateWorkspaceQuotaCommand command,
        CancellationToken cancellationToken)
    {
        if (command.StorageQuotaBytes < 0)
        {
            return Result<AdminWorkspaceDto>.Failure("Dung lượng lưu trữ tối đa (Quota) không được là số âm.");
        }

        var workspace = await _dbContext.StudioWorkspaces
            .Include(w => w.Owner)
            .Include(w => w.Members.Where(m => !m.IsDeleted))
            .Include(w => w.Series.Where(s => !s.IsDeleted))
            .FirstOrDefaultAsync(w => w.Id == command.WorkspaceId && !w.IsDeleted, cancellationToken);

        if (workspace is null)
        {
            return Result<AdminWorkspaceDto>.Failure("Không tìm thấy Studio Workspace.", ResultErrorCodes.NotFound);
        }

        workspace.SetStorageQuota(command.StorageQuotaBytes);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new AdminWorkspaceDto(
            Id: workspace.Id,
            Name: workspace.Name,
            OwnerId: workspace.OwnerId,
            OwnerName: workspace.Owner != null ? workspace.Owner.FullName : string.Empty,
            OwnerEmail: workspace.Owner != null ? workspace.Owner.Email : string.Empty,
            StorageQuotaBytes: workspace.StorageQuotaBytes,
            UsedStorageBytes: workspace.UsedStorageBytes,
            MemberCount: workspace.Members.Count(m => !m.IsDeleted),
            SeriesCount: workspace.Series.Count(s => !s.IsDeleted),
            CreatedAt: workspace.CreatedAt
        );

        return Result<AdminWorkspaceDto>.Success(dto);
    }
}
