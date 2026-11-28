using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Application.Features.Quotas.Queries.GetAdminResources;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Commands.UpdateWorkspaceResources;

/// <summary>
/// PUT /api/admin/workspaces/{id}/resources — Admin chỉnh cấp phát một Studio ở màn Tài nguyên (OPS-23).
/// null = giữ nguyên, 0 = không giới hạn (chỉ Admin đặt được, OPS-01). Bắt buộc lý do: ngoài audit tự động,
/// ghi thêm <c>RESOURCE_ALLOCATION_UPDATED</c> có giá trị cũ, mới và lý do (BR-18). Hạ hạn mức không xoá file.
/// </summary>
public sealed record UpdateWorkspaceResourcesCommand(
    Guid WorkspaceId, Guid AdminId, long? StorageQuotaBytes, long? MonthlyTokenQuota, long? MonthlyImageQuota,
    bool? AiEnabled, string Reason)
    : IRequest<Result<AdminWorkspaceResourceDto>>;

public sealed class UpdateWorkspaceResourcesCommandHandler(IPanelForgeDbContext db, WorkspaceDefaultsSettings defaults)
    : IRequestHandler<UpdateWorkspaceResourcesCommand, Result<AdminWorkspaceResourceDto>>
{
    public const int MaxReasonLength = 1000;

    public async Task<Result<AdminWorkspaceResourceDto>> Handle(UpdateWorkspaceResourcesCommand command, CancellationToken ct)
    {
        if (command.StorageQuotaBytes < 0 || command.MonthlyTokenQuota < 0 || command.MonthlyImageQuota < 0)
            return Fail("Hạn mức không được là số âm.", ResultErrorCodes.Validation);
        var reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
            return Fail("Vui lòng nhập lý do điều chỉnh.", ResultErrorCodes.Validation);
        if (reason.Length > MaxReasonLength)
            return Fail($"Lý do tối đa {MaxReasonLength} ký tự.", ResultErrorCodes.Validation);

        var workspace = await db.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == command.WorkspaceId, ct);
        if (workspace is null)
            return Fail("Không tìm thấy Studio.", ResultErrorCodes.NotFound);

        var ai = await WorkspaceQuotas.LoadAiConfigAsync(db, workspace.Id, ct);
        var before = Snapshot(workspace, ai);

        if (command.StorageQuotaBytes is { } storage)
            ai = WorkspaceQuotas.Apply(db, QuotaResourceType.Storage, storage, workspace, ai);
        if (command.MonthlyTokenQuota is { } tokens)
            ai = WorkspaceQuotas.Apply(db, QuotaResourceType.AiTokens, tokens, workspace, ai);
        if (command.MonthlyImageQuota is { } images)
            ai = WorkspaceQuotas.Apply(db, QuotaResourceType.AiImages, images, workspace, ai);
        if (command.AiEnabled is { } enabled)
        {
            if (ai is null)
            {
                ai = WorkspaceAiConfig.Create(workspace.Id);
                db.WorkspaceAiConfigs.Add(ai);
            }
            ai.UpdateSettings(ai.Provider, null, enabled, ai.MonthlyTokenQuota, string.Empty);
        }

        db.AuditLogs.Add(AuditLog.Create(
            "RESOURCE_ALLOCATION_UPDATED", nameof(StudioWorkspace), workspace.Id.ToString(), workspace.Id, command.AdminId,
            changesJson: JsonSerializer.Serialize(new { old = before, @new = Snapshot(workspace, ai) }),
            details: reason));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("Studio vừa được cập nhật bởi người khác. Vui lòng tải lại.", ResultErrorCodes.Conflict);
        }

        var policy = await QuotaPolicies.GetEffectiveAsync(db, defaults, ct);
        var rows = await GetAdminResourcesQueryHandler.LoadAsync(db, workspace.Id, policy.WarningThresholdPercent, ct);
        return Result<AdminWorkspaceResourceDto>.Success(rows.Single());
    }

    private static object Snapshot(StudioWorkspace w, WorkspaceAiConfig? ai) => new
    {
        w.StorageQuotaBytes,
        MonthlyTokenQuota = ai?.MonthlyTokenQuota,
        MonthlyImageQuota = ai?.MonthlyImageQuota,
        AiEnabled = ai?.IsEnabled
    };

    private static Result<AdminWorkspaceResourceDto> Fail(string message, string code)
        => Result<AdminWorkspaceResourceDto>.Failure(message, code);
}
