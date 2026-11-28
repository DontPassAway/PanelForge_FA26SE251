using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Application.Features.Quotas.Queries.GetAdminResources;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Commands.UpdateWorkspaceResources;

/// <summary>
/// PUT /api/admin/workspaces/{id}/resources — Admin sửa nhanh hạn mức một Studio ở màn Tài nguyên (OPS-23).
/// null = giữ nguyên, 0 = không giới hạn (chỉ Admin đặt được, OPS-01). Audit tự ghi ở SaveChanges (BR-18).
/// </summary>
public sealed record UpdateWorkspaceResourcesCommand(
    Guid WorkspaceId, long? StorageQuotaBytes, long? MonthlyTokenQuota, long? MonthlyImageQuota)
    : IRequest<Result<AdminWorkspaceResourceDto>>;

public sealed class UpdateWorkspaceResourcesCommandHandler(IPanelForgeDbContext db)
    : IRequestHandler<UpdateWorkspaceResourcesCommand, Result<AdminWorkspaceResourceDto>>
{
    public async Task<Result<AdminWorkspaceResourceDto>> Handle(UpdateWorkspaceResourcesCommand command, CancellationToken ct)
    {
        if (command.StorageQuotaBytes < 0 || command.MonthlyTokenQuota < 0 || command.MonthlyImageQuota < 0)
            return Result<AdminWorkspaceResourceDto>.Failure("Hạn mức không được là số âm.", ResultErrorCodes.Validation);

        var workspace = await db.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == command.WorkspaceId, ct);
        if (workspace is null)
            return Result<AdminWorkspaceResourceDto>.Failure("Không tìm thấy Studio.", ResultErrorCodes.NotFound);

        var ai = await WorkspaceQuotas.LoadAiConfigAsync(db, workspace.Id, ct);
        if (command.StorageQuotaBytes is { } storage)
            ai = WorkspaceQuotas.Apply(db, QuotaResourceType.Storage, storage, workspace, ai);
        if (command.MonthlyTokenQuota is { } tokens)
            ai = WorkspaceQuotas.Apply(db, QuotaResourceType.AiTokens, tokens, workspace, ai);
        if (command.MonthlyImageQuota is { } images)
            WorkspaceQuotas.Apply(db, QuotaResourceType.AiImages, images, workspace, ai);

        await db.SaveChangesAsync(ct);

        var rows = await GetAdminResourcesQueryHandler.LoadAsync(db, workspace.Id, ct);
        return Result<AdminWorkspaceResourceDto>.Success(rows.Single());
    }
}
