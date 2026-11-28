using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Resources.Common;
using PanelForge.Application.Features.Resources.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Resources.Commands.UpdateStudioResources;

/// <summary>
/// PUT /api/admin/workspaces/{id}/resources — Admin chỉnh cấp phát Storage + AI của một Studio trong một lần lưu.
/// Bắt buộc lý do; ghi Audit RESOURCE_ALLOCATION_UPDATED (giá trị cũ/mới + lý do). Hạ quota không xoá file.
/// </summary>
public sealed record UpdateStudioResourcesCommand(Guid WorkspaceId, Guid AdminId, UpdateStudioResourcesRequest Request)
    : IRequest<Result<bool>>;

public sealed class UpdateStudioResourcesCommandHandler : IRequestHandler<UpdateStudioResourcesCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _db;
    public UpdateStudioResourcesCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<bool>> Handle(UpdateStudioResourcesCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;
        if (r.StorageQuotaBytes < 0 || r.MonthlyTokenQuota < 0 || r.MonthlyImageQuota < 0)
            return Result<bool>.Failure("Hạn mức không được là số âm.");
        if (string.IsNullOrWhiteSpace(r.Reason))
            return Result<bool>.Failure("Vui lòng nhập lý do điều chỉnh.");
        if (r.Reason.Trim().Length > ResourceRequest.MaxReasonLength)
            return Result<bool>.Failure($"Lý do tối đa {ResourceRequest.MaxReasonLength} ký tự.");

        var workspace = await _db.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == cmd.WorkspaceId, ct);
        if (workspace is null)
            return Result<bool>.Failure("Không tìm thấy Studio.", ResultErrorCodes.NotFound);

        var ai = await _db.WorkspaceAiConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == workspace.Id, ct);
        if (ai is null)
        {
            ai = ResourceAllocation.NewAiConfig(workspace.Id, await ResourceAllocation.GetPolicyAsync(_db, ct));
            _db.WorkspaceAiConfigs.Add(ai);
        }

        var before = Snapshot(workspace, ai);
        workspace.SetStorageQuota(r.StorageQuotaBytes);
        ai.UpdateSettings(ai.Provider, null, r.AiEnabled, r.MonthlyTokenQuota, string.Empty);
        ai.SetMonthlyImageQuota(r.MonthlyImageQuota);
        var after = Snapshot(workspace, ai);

        _db.AuditLogs.Add(AuditLog.Create(
            "RESOURCE_ALLOCATION_UPDATED", nameof(StudioWorkspace), workspace.Id.ToString(), workspace.Id, cmd.AdminId,
            changesJson: JsonSerializer.Serialize(new { old = before, @new = after }),
            details: r.Reason.Trim()));

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<bool>.Failure("Studio vừa được cập nhật bởi người khác. Vui lòng tải lại.", ResultErrorCodes.Conflict);
        }
        return Result<bool>.Success(true);
    }

    private static object Snapshot(StudioWorkspace w, WorkspaceAiConfig ai) => new
    {
        StorageQuotaBytes = w.StorageQuotaBytes,
        MonthlyTokenQuota = ai.MonthlyTokenQuota,
        MonthlyImageQuota = ai.MonthlyImageQuota,
        AiEnabled = ai.IsEnabled
    };
}
