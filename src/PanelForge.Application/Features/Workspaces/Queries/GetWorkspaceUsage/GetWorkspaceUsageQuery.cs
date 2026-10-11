using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceUsage;

/// <summary>Một thước đo mức dùng so với giới hạn. Quota 0 = không giới hạn (PercentUsed = null).</summary>
public sealed record UsageMeterDto(long Used, long Quota, bool IsUnlimited, double? PercentUsed);

/// <param name="AiTokens">Null nếu Workspace chưa có cấu hình AI (query này không tự tạo cấu hình).</param>
/// <param name="Images">Ảnh sinh ra trong tháng so với quota; null nếu chưa có cấu hình AI. Quota 0 = không giới hạn.</param>
/// <param name="ActiveSessions">Số lượng thiết bị/phiên đang đăng nhập đồng thời của Studio so với quota.</param>
/// <param name="ActiveMemberCount">Số lượng thành viên đang có ít nhất 1 phiên hoạt động.</param>
/// <param name="MaxConcurrentSessionsPerUser">Chính sách số phiên đồng thời tối đa cho mỗi thành viên.</param>
public sealed record WorkspaceUsageDto(
    Guid WorkspaceId,
    UsageMeterDto Storage,
    UsageMeterDto? AiTokens,
    bool? AiEnabled,
    UsageMeterDto? Images = null,
    UsageMeterDto? ActiveSessions = null,
    int? ActiveMemberCount = null,
    int? MaxConcurrentSessionsPerUser = null);

public sealed record GetWorkspaceUsageQuery(Guid WorkspaceId) : IRequest<Result<WorkspaceUsageDto>>;

public sealed class GetWorkspaceUsageQueryHandler : IRequestHandler<GetWorkspaceUsageQuery, Result<WorkspaceUsageDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetWorkspaceUsageQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<WorkspaceUsageDto>> Handle(GetWorkspaceUsageQuery q, CancellationToken ct)
    {
        var workspace = await _db.StudioWorkspaces.AsNoTracking()
            .Where(w => w.Id == q.WorkspaceId)
            .Select(w => new { w.StorageQuotaBytes, w.UsedStorageBytes, w.MaxConcurrentSessions })
            .FirstOrDefaultAsync(ct);
        if (workspace is null)
            return Result<WorkspaceUsageDto>.Failure("Workspace không tồn tại.", ResultErrorCodes.NotFound);

        var ai = await _db.WorkspaceAiConfigs.AsNoTracking()
            .Where(c => c.WorkspaceId == q.WorkspaceId)
            .Select(c => new { c.MonthlyTokenQuota, c.UsedTokensCurrentMonth, c.IsEnabled, c.MonthlyImageQuota, c.UsedImagesCurrentMonth })
            .FirstOrDefaultAsync(ct);

        var memberUserIds = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == q.WorkspaceId)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var activeTokens = await _db.RefreshTokens.AsNoTracking()
            .Where(t => memberUserIds.Contains(t.UserId) && t.RevokedAt == null && t.ExpiresAt > now)
            .Select(t => new { t.UserId, t.SessionId })
            .ToListAsync(ct);

        var activeSessionsCount = activeTokens.Select(t => t.SessionId).Distinct().Count();
        var activeMembersCount = activeTokens.Select(t => t.UserId).Distinct().Count();

        long sessionQuota = workspace.MaxConcurrentSessions.HasValue && workspace.MaxConcurrentSessions.Value > 0
            ? workspace.MaxConcurrentSessions.Value * Math.Max(1, memberUserIds.Count)
            : 0;

        return Result<WorkspaceUsageDto>.Success(new WorkspaceUsageDto(
            q.WorkspaceId,
            Meter(workspace.UsedStorageBytes, workspace.StorageQuotaBytes),
            ai is null ? null : Meter(ai.UsedTokensCurrentMonth, ai.MonthlyTokenQuota),
            ai?.IsEnabled,
            ai is null ? null : Meter(ai.UsedImagesCurrentMonth, ai.MonthlyImageQuota),
            Meter(activeSessionsCount, sessionQuota),
            activeMembersCount,
            workspace.MaxConcurrentSessions));
    }

    private static UsageMeterDto Meter(long used, long quota) =>
        quota <= 0
            ? new UsageMeterDto(used, 0, true, null)
            : new UsageMeterDto(used, quota, false, Math.Round(used * 100.0 / quota, 1));
}
