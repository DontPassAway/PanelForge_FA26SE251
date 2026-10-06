using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceUsage;

/// <summary>Một thước đo mức dùng so với giới hạn. Quota 0 = không giới hạn (PercentUsed = null).</summary>
public sealed record UsageMeterDto(long Used, long Quota, bool IsUnlimited, double? PercentUsed);

/// <param name="AiTokens">Null nếu Workspace chưa có cấu hình AI (query này không tự tạo cấu hình).</param>
/// <param name="Images">Ảnh sinh ra trong tháng so với quota; null nếu chưa có cấu hình AI. Quota 0 = không giới hạn.</param>
public sealed record WorkspaceUsageDto(
    Guid WorkspaceId, UsageMeterDto Storage, UsageMeterDto? AiTokens, bool? AiEnabled, UsageMeterDto? Images = null);

public sealed record GetWorkspaceUsageQuery(Guid WorkspaceId) : IRequest<Result<WorkspaceUsageDto>>;

public sealed class GetWorkspaceUsageQueryHandler : IRequestHandler<GetWorkspaceUsageQuery, Result<WorkspaceUsageDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetWorkspaceUsageQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<WorkspaceUsageDto>> Handle(GetWorkspaceUsageQuery q, CancellationToken ct)
    {
        var workspace = await _db.StudioWorkspaces.AsNoTracking()
            .Where(w => w.Id == q.WorkspaceId)
            .Select(w => new { w.StorageQuotaBytes, w.UsedStorageBytes })
            .FirstOrDefaultAsync(ct);
        if (workspace is null)
            return Result<WorkspaceUsageDto>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        var ai = await _db.WorkspaceAiConfigs.AsNoTracking()
            .Where(c => c.WorkspaceId == q.WorkspaceId)
            .Select(c => new { c.MonthlyTokenQuota, c.UsedTokensCurrentMonth, c.IsEnabled, c.MonthlyImageQuota, c.UsedImagesCurrentMonth })
            .FirstOrDefaultAsync(ct);

        return Result<WorkspaceUsageDto>.Success(new WorkspaceUsageDto(
            q.WorkspaceId,
            Meter(workspace.UsedStorageBytes, workspace.StorageQuotaBytes),
            ai is null ? null : Meter(ai.UsedTokensCurrentMonth, ai.MonthlyTokenQuota),
            ai?.IsEnabled,
            ai is null ? null : Meter(ai.UsedImagesCurrentMonth, ai.MonthlyImageQuota)));
    }

    private static UsageMeterDto Meter(long used, long quota) =>
        quota <= 0
            ? new UsageMeterDto(used, 0, true, null)
            : new UsageMeterDto(used, quota, false, Math.Round(used * 100.0 / quota, 1));
}
