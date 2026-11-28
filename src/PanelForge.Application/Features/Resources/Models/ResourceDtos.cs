using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceUsage;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Resources.Models;

/// <param name="IsConfigured">false = chưa lưu chính sách, đang trả giá trị mặc định phát triển.</param>
public sealed record ResourcePolicyDto(
    long DefaultStorageQuotaBytes,
    long DefaultMonthlyTokenQuota,
    long DefaultMonthlyImageQuota,
    bool DefaultAiEnabled,
    int WarningThresholdPercent,
    bool IsConfigured,
    DateTime? UpdatedAt);

public sealed record UpdateResourcePolicyRequest(
    long DefaultStorageQuotaBytes,
    long DefaultMonthlyTokenQuota,
    long DefaultMonthlyImageQuota,
    bool DefaultAiEnabled,
    int WarningThresholdPercent);

/// <summary>Trạng thái tài nguyên của Studio: Ok / NearLimit (≥ ngưỡng cảnh báo) / Exhausted (≥ 100%).</summary>
public enum ResourceHealth { Ok = 0, NearLimit = 1, Exhausted = 2 }

/// <param name="AiTokens">null nếu Studio chưa có cấu hình AI.</param>
/// <param name="AiNextResetAt">Đầu tháng UTC kế tiếp (AiUsageService reset theo tháng UTC).</param>
public sealed record StudioResourceDto(
    Guid WorkspaceId,
    string Name,
    string OwnerName,
    string OwnerEmail,
    int MemberCount,
    int SeriesCount,
    DateTime CreatedAt,
    UsageMeterDto Storage,
    bool AiEnabled,
    UsageMeterDto? AiTokens,
    UsageMeterDto? AiImages,
    DateTime AiNextResetAt,
    int PendingRequests,
    ResourceHealth Health);

public sealed record ResourceOverviewDto(
    int TotalStudios,
    long TotalStorageUsedBytes,
    long TotalTokensUsedThisMonth,
    long TotalImagesThisMonth,
    int AiEnabledStudios,
    int NearLimitStudios,
    int ExhaustedStudios,
    int PendingRequests,
    int WarningThresholdPercent,
    IReadOnlyList<StudioResourceDto> Studios);

/// <summary>Điều chỉnh cấp phát của một Studio. 0 = không giới hạn. Reason bắt buộc (ghi Audit).</summary>
public sealed record UpdateStudioResourcesRequest(
    long StorageQuotaBytes,
    long MonthlyTokenQuota,
    long MonthlyImageQuota,
    bool AiEnabled,
    string Reason);

public sealed record ResourceRequestDto(
    Guid Id,
    Guid WorkspaceId,
    string WorkspaceName,
    ResourceType ResourceType,
    long CurrentLimit,
    long RequestedLimit,
    string Reason,
    Guid RequestedBy,
    string RequestedByName,
    DateTime RequestedAt,
    ResourceRequestStatus Status,
    Guid? ReviewedBy,
    string? ReviewedByName,
    DateTime? ReviewedAt,
    string? ReviewNote);

public sealed record SubmitResourceRequestRequest(ResourceType ResourceType, long RequestedLimit, string Reason);

public sealed record ReviewResourceRequestRequest(string? Note);
