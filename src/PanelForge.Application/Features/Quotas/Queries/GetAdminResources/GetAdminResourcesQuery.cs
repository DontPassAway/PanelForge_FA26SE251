using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceUsage;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Queries.GetAdminResources;

/// <summary>
/// GET /api/admin/resources — mỗi Studio một dòng: dung lượng, token và ảnh AI đã dùng so với mức cấp, trạng thái
/// Bình thường / Gần hết / Đã hết theo ngưỡng của chính sách, số yêu cầu đang chờ; kèm số liệu tổng cho Overview (OPS-23).
/// Chỉ số liệu vận hành, không đọc nội dung sản xuất (BR-07).
/// </summary>
public sealed record GetAdminResourcesQuery : IRequest<Result<AdminResourceOverviewDto>>;

public sealed class GetAdminResourcesQueryHandler(IPanelForgeDbContext db, WorkspaceDefaultsSettings defaults)
    : IRequestHandler<GetAdminResourcesQuery, Result<AdminResourceOverviewDto>>
{
    public async Task<Result<AdminResourceOverviewDto>> Handle(GetAdminResourcesQuery q, CancellationToken ct)
    {
        var policy = await QuotaPolicies.GetEffectiveAsync(db, defaults, ct);
        var studios = await LoadAsync(db, null, policy.WarningThresholdPercent, ct);

        return Result<AdminResourceOverviewDto>.Success(new AdminResourceOverviewDto(
            TotalStudios: studios.Count,
            TotalStorageUsedBytes: studios.Sum(s => s.Storage.Used),
            TotalTokensUsedThisMonth: studios.Sum(s => s.AiTokens?.Used ?? 0),
            TotalImagesThisMonth: studios.Sum(s => s.AiImages?.Used ?? 0),
            AiEnabledStudios: studios.Count(s => s.AiEnabled),
            NearLimitStudios: studios.Count(s => s.Health == ResourceHealth.NearLimit),
            ExhaustedStudios: studios.Count(s => s.Health == ResourceHealth.Exhausted),
            PendingRequests: studios.Sum(s => s.PendingRequestCount),
            WarningThresholdPercent: policy.WarningThresholdPercent,
            Studios: studios,
            TotalRequestsThisMonth: studios.Sum(s => s.AiRequests?.Used ?? 0)));
    }

    internal static async Task<List<AdminWorkspaceResourceDto>> LoadAsync(
        IPanelForgeDbContext db, Guid? workspaceId, int warningPercent, CancellationToken ct)
    {
        var workspaces = db.StudioWorkspaces.AsNoTracking();
        if (workspaceId is { } id) workspaces = workspaces.Where(w => w.Id == id);

        var rows = await workspaces
            .OrderBy(w => w.Name)
            .Select(w => new
            {
                w.Id,
                w.Name,
                OwnerName = w.Owner.FullName,
                OwnerEmail = w.Owner.Email,
                Members = w.Members.Count(m => !m.IsDeleted),
                Series = w.Series.Count(s => !s.IsDeleted),
                w.CreatedAt,
                w.StorageQuotaBytes,
                w.UsedStorageBytes,
                Ai = db.WorkspaceAiConfigs.Where(c => c.WorkspaceId == w.Id)
                    .Select(c => new { c.IsEnabled, c.MonthlyTokenQuota, c.UsedTokensCurrentMonth, c.MonthlyImageQuota, c.UsedImagesCurrentMonth,
                        c.MonthlyRequestQuota, c.UsedRequestsCurrentMonth, c.RequestsPerMinuteLimit, c.LastResetAt, c.CreatedAt })
                    .FirstOrDefault(),
                Pending = db.QuotaIncreaseRequests.Count(r => r.WorkspaceId == w.Id && r.Status == QuotaIncreaseRequestStatus.Pending)
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var nextReset = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        return rows.Select(r =>
        {
            // OPS-22: bộ đếm của tháng trước (chưa có lượt gọi AI nào trong tháng này) hiện là 0
            var counted = r.Ai is not null && WorkspaceAiConfig.IsCountedThisMonth(r.Ai.LastResetAt, r.Ai.CreatedAt, now);
            var storage = Meter(r.UsedStorageBytes, r.StorageQuotaBytes);
            var tokens = r.Ai is null ? null : Meter(counted ? r.Ai.UsedTokensCurrentMonth : 0, r.Ai.MonthlyTokenQuota);
            var images = r.Ai is null ? null : Meter(counted ? r.Ai.UsedImagesCurrentMonth : 0, r.Ai.MonthlyImageQuota);
            var requests = r.Ai is null ? null : Meter(counted ? r.Ai.UsedRequestsCurrentMonth : 0, r.Ai.MonthlyRequestQuota);
            return new AdminWorkspaceResourceDto(
                r.Id, r.Name, r.OwnerName, r.OwnerEmail, r.Members, r.Series, r.CreatedAt,
                storage, r.Ai?.IsEnabled ?? false, tokens, images, nextReset, r.Pending,
                Health(warningPercent, storage.PercentUsed, tokens?.PercentUsed, images?.PercentUsed, requests?.PercentUsed),
                requests, r.Ai?.RequestsPerMinuteLimit);
        }).ToList();
    }

    private static UsageMeterDto Meter(long used, long quota)
        => quota <= 0
            ? new UsageMeterDto(used, quota, true, null)
            : new UsageMeterDto(used, quota, false, Math.Round(used * 100.0 / quota, 1));

    private static ResourceHealth Health(int warningPercent, params double?[] percents)
    {
        var worst = percents.Where(p => p.HasValue).Select(p => p!.Value).DefaultIfEmpty(0).Max();
        return worst >= 100 ? ResourceHealth.Exhausted
            : worst >= warningPercent ? ResourceHealth.NearLimit
            : ResourceHealth.Ok;
    }
}
