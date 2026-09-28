using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;
using PanelForge.Application.Features.AiUsage.Models;
using PanelForge.Application.Features.AiUsage.Common;

namespace PanelForge.Application.Features.AiUsage.Queries.GetAiUsageReport;

/// <param name="From">UTC, bao gồm. Mặc định: đầu tháng hiện tại.</param>
/// <param name="To">UTC, không bao gồm. Mặc định: thời điểm hiện tại.</param>
public sealed record GetAiUsageReportQuery(Guid? WorkspaceId = null, DateTime? From = null, DateTime? To = null)
    : IRequest<Result<IReadOnlyList<AiUsageReportItemDto>>>;

public sealed class GetAiUsageReportQueryHandler
    : IRequestHandler<GetAiUsageReportQuery, Result<IReadOnlyList<AiUsageReportItemDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetAiUsageReportQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<AiUsageReportItemDto>>> Handle(GetAiUsageReportQuery q, CancellationToken ct)
    {
        var (from, to, error) = AiUsageFiltering.ResolvePeriod(q.From, q.To);
        if (error is not null) return Result<IReadOnlyList<AiUsageReportItemDto>>.Failure(error);

        var configs = await _dbContext.WorkspaceAiConfigs.AsNoTracking()
            .Where(c => !q.WorkspaceId.HasValue || c.WorkspaceId == q.WorkspaceId.Value)
            .Select(c => new
            {
                c.WorkspaceId,
                c.Provider,
                c.MonthlyTokenQuota,
                c.UsedTokensCurrentMonth,
                c.IsEnabled
            })
            .ToListAsync(ct);

        var byFeature = await _dbContext.AiUsageRecords.AsNoTracking()
            .Where(r => r.OccurredAtUtc >= from && r.OccurredAtUtc < to)
            .Where(r => !q.WorkspaceId.HasValue || r.WorkspaceId == q.WorkspaceId.Value)
            .GroupBy(r => new { r.WorkspaceId, r.Feature, r.Status })
            .Select(g => new
            {
                g.Key.WorkspaceId,
                g.Key.Feature,
                g.Key.Status,
                Calls = g.Count(),
                Tokens = g.Sum(r => r.TotalTokens),
                Cost = g.Sum(r => r.CostUsd ?? 0m)
            })
            .ToListAsync(ct);

        // Workspace có cấu hình AI hoặc có phát sinh bản ghi trong kỳ
        var workspaceIds = configs.Select(c => c.WorkspaceId)
            .Concat(byFeature.Select(r => r.WorkspaceId))
            .Distinct()
            .ToList();

        var names = await _dbContext.StudioWorkspaces.AsNoTracking()
            .Where(w => workspaceIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Name })
            .ToDictionaryAsync(w => w.Id, w => w.Name, ct);

        var report = workspaceIds.Select(wsId =>
        {
            var c = configs.FirstOrDefault(x => x.WorkspaceId == wsId);
            var rows = byFeature.Where(r => r.WorkspaceId == wsId).ToList();
            var quota = c?.MonthlyTokenQuota ?? 0;
            var used = c?.UsedTokensCurrentMonth ?? 0;

            return new AiUsageReportItemDto(
                WorkspaceId: wsId,
                WorkspaceName: names.GetValueOrDefault(wsId, string.Empty),
                Provider: c?.Provider,
                MonthlyTokenQuota: quota,
                UsedTokensCurrentMonth: used,
                UsagePercent: quota > 0 ? (double)used / quota * 100 : 0,
                IsEnabled: c?.IsEnabled ?? false,
                PeriodFrom: from,
                PeriodTo: to,
                CallCount: rows.Sum(r => r.Calls),
                SucceededCount: rows.Where(r => r.Status == AiUsageStatus.Succeeded).Sum(r => r.Calls),
                FailedCount: rows.Where(r => r.Status is AiUsageStatus.Failed or AiUsageStatus.ProviderUnavailable).Sum(r => r.Calls),
                BlockedCount: rows.Where(r => r.Status is AiUsageStatus.QuotaExceeded or AiUsageStatus.Disabled).Sum(r => r.Calls),
                TotalTokens: rows.Sum(r => r.Tokens),
                TotalCostUsd: rows.Sum(r => r.Cost),
                ByFeature: rows.GroupBy(r => r.Feature)
                    .Select(g => new AiUsageByFeatureDto(g.Key, g.Sum(r => r.Calls), g.Sum(r => r.Tokens)))
                    .OrderByDescending(f => f.TotalTokens)
                    .ToList());
        })
        .OrderBy(r => r.WorkspaceName)
        .ToList();

        return Result<IReadOnlyList<AiUsageReportItemDto>>.Success(report);
    }
}
