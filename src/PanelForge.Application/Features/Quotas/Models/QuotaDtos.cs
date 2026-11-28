using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceUsage;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Models;

/// <param name="IsFromSettings">true khi Admin chưa lưu chính sách nào: đang dùng giá trị trong cấu hình server.</param>
public sealed record QuotaPolicyDto(
    long StorageQuotaBytes,
    long MonthlyTokenQuota,
    long MonthlyImageQuota,
    bool AiEnabledByDefault,
    int WarningThresholdPercent,
    bool IsFromSettings,
    DateTime? UpdatedAt,
    string? UpdatedByName,
    long MonthlyRequestQuota = 0,
    int RequestsPerMinute = 0);

public sealed record UpdateQuotaPolicyRequest(
    long StorageQuotaBytes,
    long MonthlyTokenQuota,
    long MonthlyImageQuota,
    bool AiEnabledByDefault = true,
    int WarningThresholdPercent = 80,
    long MonthlyRequestQuota = QuotaPolicyDefaults.MonthlyRequestQuota,
    int RequestsPerMinute = QuotaPolicyDefaults.RequestsPerMinute);

internal static class QuotaPolicyDefaults
{
    public const long MonthlyRequestQuota = PanelForge.Domain.Entities.Workspaces.QuotaPolicy.DefaultMonthlyRequestQuota;
    public const int RequestsPerMinute = PanelForge.Domain.Entities.Workspaces.QuotaPolicy.DefaultRequestsPerMinute;
}

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

/// <summary>Trạng thái tài nguyên của Studio: Ok / NearLimit (≥ ngưỡng cảnh báo) / Exhausted (≥ 100%).</summary>
public enum ResourceHealth { Ok = 0, NearLimit = 1, Exhausted = 2 }

/// <summary>
/// Một dòng ở màn Tài nguyên của Admin (OPS-23). Quota 0 = không giới hạn. AiTokens/AiImages null = Studio chưa có cấu hình AI.
/// AiNextResetAt: đầu tháng UTC kế tiếp (AiUsageService đặt lại bộ đếm theo tháng UTC).
/// </summary>
public sealed record AdminWorkspaceResourceDto(
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
    int PendingRequestCount,
    ResourceHealth Health,
    UsageMeterDto? AiRequests = null,
    int? RequestsPerMinuteLimit = null);

/// <summary>GET /api/admin/resources: số liệu tổng cho Overview + từng Studio. Đọc thẳng bộ đếm, không ước tính.</summary>
public sealed record AdminResourceOverviewDto(
    int TotalStudios,
    long TotalStorageUsedBytes,
    long TotalTokensUsedThisMonth,
    long TotalImagesThisMonth,
    int AiEnabledStudios,
    int NearLimitStudios,
    int ExhaustedStudios,
    int PendingRequests,
    int WarningThresholdPercent,
    IReadOnlyList<AdminWorkspaceResourceDto> Studios,
    long TotalRequestsThisMonth = 0);

/// <summary>
/// Admin chỉnh cấp phát một Studio. null = giữ nguyên; 0 = không giới hạn. Reason bắt buộc, ghi vào Nhật ký hệ thống.
/// Hạ hạn mức dưới mức đang dùng không xoá file, chỉ chặn ghi thêm.
/// </summary>
public sealed record UpdateWorkspaceResourcesRequest(
    long? StorageQuotaBytes,
    long? MonthlyTokenQuota,
    long? MonthlyImageQuota,
    bool? AiEnabled,
    string? Reason,
    long? MonthlyRequestQuota = null,
    int? RequestsPerMinute = null);

/// <summary>
/// Kết quả đối soát kho file (OPS-22). Không xoá file nào: chỉ so dung lượng đang ghi với dung lượng file còn được dùng,
/// và liệt kê file mồ côi. <see cref="Applied"/> = true khi đã ghi dung lượng tính lại vào từng Studio.
/// </summary>
public sealed record StorageReconcileReportDto(
    DateTime ScannedAt,
    bool Applied,
    int ScannedFiles,
    long ScannedBytes,
    IReadOnlyList<StorageReconcileWorkspaceDto> Workspaces,
    int UnattributedFiles,
    long UnattributedBytes,
    IReadOnlyList<StorageOrphanFileDto> OrphanSamples,
    bool OrphanSamplesTruncated);

/// <summary>
/// Một Studio trong báo cáo đối soát. ComputedBytes = tổng file gốc còn được tham chiếu (artwork, ảnh bìa, ảnh Bible).
/// DerivedBytes = ảnh thu nhỏ và file xuất bản, không tính vào hạn mức. MissingFiles = file DB còn trỏ tới nhưng kho không có.
/// </summary>
public sealed record StorageReconcileWorkspaceDto(
    Guid WorkspaceId,
    string Name,
    long RecordedBytes,
    long ComputedBytes,
    int OrphanFiles,
    long OrphanBytes,
    long DerivedBytes,
    int MissingFiles);

/// <summary>File không còn bản ghi nào tham chiếu. WorkspaceId null = không xác định được Studio từ khoá hoặc Studio đã xoá.</summary>
public sealed record StorageOrphanFileDto(string Key, long SizeBytes, Guid? WorkspaceId, DateTime LastModifiedUtc);

/// <summary>
/// Kết quả xoá file mồ côi (OPS-22). Chỉ xoá file thuộc Studio đang hoạt động và cũ hơn <see cref="MinAgeDays"/> ngày;
/// file chưa xác định Studio và file vừa được dùng lại trong lúc quét thì giữ. FailedFiles = kho báo lỗi khi xoá.
/// </summary>
public sealed record DeleteOrphanFilesResultDto(
    int DeletedFiles,
    long DeletedBytes,
    int SkippedRecentFiles,
    int SkippedUnattributedFiles,
    int SkippedReferencedFiles,
    int FailedFiles,
    int MinAgeDays);
