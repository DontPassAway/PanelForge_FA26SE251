using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiUsage;

// UC-15: báo cáo AI usage theo workspace so với hạn mức (E-29 AIUsageRecord).

// ─── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Một dòng báo cáo cho một workspace. Các field quota giữ nguyên như trước (tương thích FE cũ);
/// các field Period* tổng hợp từ ai_usage_records trong khoảng [PeriodFrom, PeriodTo).
/// </summary>
public sealed record AiUsageReportItemDto(
    Guid WorkspaceId,
    string WorkspaceName,
    string? Provider,
    long MonthlyTokenQuota,
    long UsedTokensCurrentMonth,
    double UsagePercent,
    bool IsEnabled,
    DateTime PeriodFrom,
    DateTime PeriodTo,
    int CallCount,
    int SucceededCount,
    int FailedCount,
    int BlockedCount,
    long TotalTokens,
    decimal TotalCostUsd,
    IReadOnlyList<AiUsageByFeatureDto> ByFeature);

public sealed record AiUsageByFeatureDto(string Feature, int CallCount, long TotalTokens);

public sealed record AiUsageRecordDto(
    Guid Id,
    Guid WorkspaceId,
    Guid? UserId,
    string Feature,
    string Provider,
    string? Model,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    decimal? CostUsd,
    AiUsageStatus Status,
    string? ErrorMessage,
    int? DurationMs,
    DateTime OccurredAtUtc);

public sealed record AiUsageRecordsPagedResponse(int TotalCount, int Page, int PageSize, IReadOnlyList<AiUsageRecordDto> Items);

// ─── Report ───────────────────────────────────────────────────────────────────

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

// ─── Records (chi tiết) ───────────────────────────────────────────────────────

public sealed record GetAiUsageRecordsQuery(
    Guid? WorkspaceId = null,
    Guid? UserId = null,
    string? Feature = null,
    AiUsageStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 30
) : IRequest<Result<AiUsageRecordsPagedResponse>>;

public sealed class GetAiUsageRecordsQueryHandler
    : IRequestHandler<GetAiUsageRecordsQuery, Result<AiUsageRecordsPagedResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetAiUsageRecordsQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<AiUsageRecordsPagedResponse>> Handle(GetAiUsageRecordsQuery q, CancellationToken ct)
    {
        if (q.From.HasValue && q.To.HasValue && q.From.Value >= q.To.Value)
            return Result<AiUsageRecordsPagedResponse>.Failure("Khoảng thời gian không hợp lệ: 'from' phải nhỏ hơn 'to'.");

        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 1, 100);
        var query = _dbContext.AiUsageRecords.AsNoTracking().ApplyFilter(q);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(AiUsageFiltering.ToDto)
            .ToListAsync(ct);

        return Result<AiUsageRecordsPagedResponse>.Success(new AiUsageRecordsPagedResponse(total, page, pageSize, items));
    }
}

// ─── Export ───────────────────────────────────────────────────────────────────

public sealed record ExportAiUsageRecordsQuery(GetAiUsageRecordsQuery Filter) : IRequest<Result<CsvExport>>
{
    public const int MaxRows = 10_000;
}

public sealed class ExportAiUsageRecordsQueryHandler : IRequestHandler<ExportAiUsageRecordsQuery, Result<CsvExport>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public ExportAiUsageRecordsQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<CsvExport>> Handle(ExportAiUsageRecordsQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        if (f.From.HasValue && f.To.HasValue && f.From.Value >= f.To.Value)
            return Result<CsvExport>.Failure("Khoảng thời gian không hợp lệ: 'from' phải nhỏ hơn 'to'.");

        var rows = await _dbContext.AiUsageRecords.AsNoTracking()
            .ApplyFilter(f)
            .OrderByDescending(r => r.OccurredAtUtc)
            .Take(ExportAiUsageRecordsQuery.MaxRows + 1)
            .Select(AiUsageFiltering.ToDto)
            .ToListAsync(ct);

        var truncated = rows.Count > ExportAiUsageRecordsQuery.MaxRows;
        if (truncated) rows.RemoveAt(rows.Count - 1);

        var content = CsvWriter.Write(
            ["OccurredAtUtc", "WorkspaceId", "UserId", "Feature", "Provider", "Model", "Status",
             "PromptTokens", "CompletionTokens", "TotalTokens", "CostUsd", "DurationMs", "ErrorMessage"],
            rows.Select(r => new object?[]
            {
                r.OccurredAtUtc, r.WorkspaceId, r.UserId, r.Feature, r.Provider, r.Model, r.Status,
                r.PromptTokens, r.CompletionTokens, r.TotalTokens, r.CostUsd, r.DurationMs, r.ErrorMessage
            }));

        return Result<CsvExport>.Success(new CsvExport(
            $"ai-usage-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv", content, rows.Count, truncated));
    }
}

internal static class AiUsageFiltering
{
    public static readonly System.Linq.Expressions.Expression<Func<AiUsageRecord, AiUsageRecordDto>> ToDto = r => new AiUsageRecordDto(
        r.Id, r.WorkspaceId, r.UserId, r.Feature, r.Provider, r.Model, r.PromptTokens, r.CompletionTokens,
        r.TotalTokens, r.CostUsd, r.Status, r.ErrorMessage, r.DurationMs, r.OccurredAtUtc);

    public static IQueryable<AiUsageRecord> ApplyFilter(this IQueryable<AiUsageRecord> query, GetAiUsageRecordsQuery f)
    {
        if (f.WorkspaceId.HasValue) query = query.Where(r => r.WorkspaceId == f.WorkspaceId.Value);
        if (f.UserId.HasValue) query = query.Where(r => r.UserId == f.UserId.Value);
        if (!string.IsNullOrWhiteSpace(f.Feature))
        {
            var feature = f.Feature.Trim();
            query = query.Where(r => r.Feature == feature);
        }
        if (f.Status.HasValue) query = query.Where(r => r.Status == f.Status.Value);
        if (f.From.HasValue)
        {
            var from = DateTime.SpecifyKind(f.From.Value, DateTimeKind.Utc);
            query = query.Where(r => r.OccurredAtUtc >= from);
        }
        if (f.To.HasValue)
        {
            var to = DateTime.SpecifyKind(f.To.Value, DateTimeKind.Utc);
            query = query.Where(r => r.OccurredAtUtc < to);
        }
        return query;
    }

    /// <summary>Mặc định kỳ báo cáo = tháng hiện tại (khớp với hạn mức theo tháng).</summary>
    public static (DateTime From, DateTime To, string? Error) ResolvePeriod(DateTime? from, DateTime? to)
    {
        var now = DateTime.UtcNow;
        var resolvedFrom = from.HasValue
            ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)
            : new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var resolvedTo = to.HasValue ? DateTime.SpecifyKind(to.Value, DateTimeKind.Utc) : now.AddSeconds(1);

        return resolvedFrom >= resolvedTo
            ? (resolvedFrom, resolvedTo, "Khoảng thời gian không hợp lệ: 'from' phải nhỏ hơn 'to'.")
            : (resolvedFrom, resolvedTo, null);
    }
}
