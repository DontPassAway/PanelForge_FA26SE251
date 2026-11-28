using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Models;

/// <param name="IsFromSettings">true khi Admin chưa lưu chính sách nào: đang dùng giá trị trong cấu hình server.</param>
public sealed record QuotaPolicyDto(
    long StorageQuotaBytes,
    long MonthlyTokenQuota,
    long MonthlyImageQuota,
    bool IsFromSettings,
    DateTime? UpdatedAt,
    string? UpdatedByName);

public sealed record UpdateQuotaPolicyRequest(long StorageQuotaBytes, long MonthlyTokenQuota, long MonthlyImageQuota);

/// <param name="RequestedQuota">Mức mới muốn có (giá trị tuyệt đối).</param>
public sealed record SubmitQuotaIncreaseRequestBody(QuotaResourceType ResourceType, long RequestedQuota, string Reason);

/// <param name="GrantedQuota">null = cấp đúng mức xin.</param>
public sealed record ApproveQuotaIncreaseRequestBody(long? GrantedQuota, string? Note);

public sealed record RejectQuotaIncreaseRequestBody(string Note);

public sealed record QuotaIncreaseRequestDto(
    Guid Id,
    Guid WorkspaceId,
    string WorkspaceName,
    QuotaResourceType ResourceType,
    long CurrentQuota,
    long RequestedQuota,
    string Reason,
    QuotaIncreaseRequestStatus Status,
    Guid RequestedByUserId,
    string RequestedByName,
    DateTime CreatedAt,
    Guid? ReviewedByUserId,
    string? ReviewedByName,
    DateTime? ReviewedAt,
    string? ReviewNote,
    long? GrantedQuota);

/// <summary>Một dòng ở màn Tài nguyên của Admin (OPS-23). Quota 0 = không giới hạn.</summary>
public sealed record AdminWorkspaceResourceDto(
    Guid WorkspaceId,
    string Name,
    string OwnerName,
    string OwnerEmail,
    long StorageQuotaBytes,
    long UsedStorageBytes,
    bool HasAiConfig,
    bool AiEnabled,
    long MonthlyTokenQuota,
    long UsedTokensCurrentMonth,
    long MonthlyImageQuota,
    long UsedImagesCurrentMonth,
    int PendingRequestCount);

/// <summary>Admin sửa nhanh hạn mức một Studio. null = giữ nguyên; 0 = không giới hạn.</summary>
public sealed record UpdateWorkspaceResourcesRequest(long? StorageQuotaBytes, long? MonthlyTokenQuota, long? MonthlyImageQuota);
