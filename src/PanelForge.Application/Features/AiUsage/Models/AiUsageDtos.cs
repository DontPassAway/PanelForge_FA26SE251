using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiUsage.Models;

// UC-15: báo cáo AI usage theo workspace so với hạn mức (E-29 AIUsageRecord).

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
