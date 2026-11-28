using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Resources.Common;
using PanelForge.Application.Features.Resources.Models;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceUsage;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Resources.Queries.GetResourceOverview;

/// <summary>
/// GET /api/admin/resources — mức dùng và cấp phát Storage + AI của mọi Studio, cùng số liệu tổng cho Admin Overview.
/// Số liệu đọc thẳng từ bộ đếm thật (used_storage_bytes, used_tokens_current_month); không ước tính.
/// </summary>
public sealed record GetResourceOverviewQuery : IRequest<Result<ResourceOverviewDto>>;

public sealed class GetResourceOverviewQueryHandler : IRequestHandler<GetResourceOverviewQuery, Result<ResourceOverviewDto>>
{
    private readonly IPanelForgeDbContext _db;
    public GetResourceOverviewQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ResourceOverviewDto>> Handle(GetResourceOverviewQuery q, CancellationToken ct)
    {
        var policy = await ResourceAllocation.GetPolicyAsync(_db, ct);
        var now = DateTime.UtcNow;
        var nextReset = ResourceAllocation.NextMonthlyResetUtc(now);

        var workspaces = await _db.StudioWorkspaces.AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new
            {
                w.Id, w.Name, w.CreatedAt, w.StorageQuotaBytes, w.UsedStorageBytes,
                OwnerName = w.Owner.FullName, OwnerEmail = w.Owner.Email,
                Members = w.Members.Count(m => !m.IsDeleted),
                Series = w.Series.Count(s => !s.IsDeleted)
            })
            .ToListAsync(ct);

        var aiByWorkspace = await _db.WorkspaceAiConfigs.AsNoTracking()
            .Select(c => new { c.WorkspaceId, c.IsEnabled, c.MonthlyTokenQuota, c.UsedTokensCurrentMonth,
                               c.MonthlyImageQuota, c.UsedImagesCurrentMonth, c.LastResetAt, c.CreatedAt })
            .ToDictionaryAsync(c => c.WorkspaceId, ct);

        var pendingByWorkspace = await _db.ResourceRequests.AsNoTracking()
            .Where(r => r.Status == ResourceRequestStatus.Pending)
            .GroupBy(r => r.WorkspaceId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        var studios = workspaces.Select(w =>
        {
            aiByWorkspace.TryGetValue(w.Id, out var ai);
            // Bộ đếm chỉ được reset ở lượt gọi AI kế tiếp: tháng cũ chưa reset thì tháng này coi như 0.
            var currentPeriod = ai is not null && (ai.LastResetAt ?? ai.CreatedAt) is var last
                                && last.Year == now.Year && last.Month == now.Month;
            var storage = Meter(w.UsedStorageBytes, w.StorageQuotaBytes);
            var tokens = ai is null ? null : Meter(currentPeriod ? ai.UsedTokensCurrentMonth : 0, ai.MonthlyTokenQuota);
            var images = ai is null ? null : Meter(currentPeriod ? ai.UsedImagesCurrentMonth : 0, ai.MonthlyImageQuota);
            return new StudioResourceDto(
                w.Id, w.Name, w.OwnerName, w.OwnerEmail, w.Members, w.Series, w.CreatedAt,
                storage, ai?.IsEnabled ?? false, tokens, images, nextReset,
                pendingByWorkspace.GetValueOrDefault(w.Id),
                ResourceAllocation.Health(policy.WarningThresholdPercent, storage.PercentUsed, tokens?.PercentUsed, images?.PercentUsed));
        }).ToList();

        return Result<ResourceOverviewDto>.Success(new ResourceOverviewDto(
            TotalStudios: studios.Count,
            TotalStorageUsedBytes: studios.Sum(s => s.Storage.Used),
            TotalTokensUsedThisMonth: studios.Sum(s => s.AiTokens?.Used ?? 0),
            TotalImagesThisMonth: studios.Sum(s => s.AiImages?.Used ?? 0),
            AiEnabledStudios: studios.Count(s => s.AiEnabled),
            NearLimitStudios: studios.Count(s => s.Health == ResourceHealth.NearLimit),
            ExhaustedStudios: studios.Count(s => s.Health == ResourceHealth.Exhausted),
            PendingRequests: pendingByWorkspace.Values.Sum(),
            WarningThresholdPercent: policy.WarningThresholdPercent,
            Studios: studios));
    }

    private static UsageMeterDto Meter(long used, long quota)
        => quota <= 0
            ? new UsageMeterDto(used, quota, true, null)
            : new UsageMeterDto(used, quota, false, Math.Round(used * 100.0 / quota, 1));
}
