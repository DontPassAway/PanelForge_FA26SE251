using PanelForge.Application.Features.Audit.Models;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Admin.Models;

/// <summary>
/// GET /api/admin/users/{id} (OPS-30): hồ sơ tài khoản, phiên đăng nhập đang mở, Studio tham gia, Ví và hoạt động gần đây.
/// Chỉ dữ liệu vận hành tài khoản: không có nội dung sản xuất (BR-07) và không có mật khẩu, khoá 2FA, token.
/// </summary>
public sealed record AdminUserDetailDto(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    string? AvatarUrl,
    string Role,
    bool CanCreateStudio,
    bool IsActive,
    bool IsEmailConfirmed,
    bool IsPendingInvitation,
    bool TwoFactorEnabled,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? LastActiveAt,
    IReadOnlyList<AdminUserSessionDto> ActiveSessions,
    IReadOnlyList<AdminUserMembershipDto> Memberships,
    AdminUserWalletDto? Wallet,
    AdminUserAiUsageDto AiUsageThisMonth,
    IReadOnlyList<AuditLogDto> RecentActivity);

public sealed record AdminUserSessionDto(
    Guid SessionId,
    string? DeviceName,
    string? IpAddress,
    string? UserAgent,
    DateTime StartedAt,
    DateTime LastActiveAt,
    DateTime ExpiresAt);

public sealed record AdminUserMembershipDto(
    Guid WorkspaceId,
    string WorkspaceName,
    WorkspaceRole Role,
    WorkspaceMemberStatus Status,
    bool IsOwner,
    DateTime JoinedAt);

/// <param name="TopUpTotalVnd">Tổng tiền nạp đã được duyệt.</param>
/// <param name="PendingTopUps">Đơn nạp VietQR đang chờ Publisher duyệt.</param>
public sealed record AdminUserWalletDto(
    decimal BalanceVnd,
    decimal TopUpTotalVnd,
    int UnlockedEditions,
    int PendingTopUps);

/// <param name="FailedCalls">Lượt gọi lỗi, hết hạn mức hoặc nhà cung cấp không phản hồi.</param>
public sealed record AdminUserAiUsageDto(long Tokens, int Calls, int FailedCalls);

/// <summary>
/// GET /api/admin/workspaces/{id} (OPS-31): dòng tài nguyên như màn Tài nguyên, chủ Studio, thành viên, danh sách Series
/// (chỉ tên, số chương, trạng thái phát hành), yêu cầu tăng hạn mức, AI 30 ngày và Nhật ký gần đây. Không đọc kịch bản,
/// Bible hay ảnh (BR-07).
/// </summary>
public sealed record AdminWorkspaceDetailDto(
    AdminWorkspaceResourceDto Resources,
    Guid OwnerId,
    string? AiProvider,
    int? SessionLifetimeMinutes,
    int? SessionIdleTimeoutMinutes,
    int? MaxConcurrentSessions,
    IReadOnlyList<AdminWorkspaceMemberDto> Members,
    IReadOnlyList<AdminWorkspaceSeriesDto> Series,
    IReadOnlyList<QuotaIncreaseRequestDto> QuotaRequests,
    AdminWorkspaceAiActivityDto AiLast30Days,
    IReadOnlyList<AuditLogDto> RecentActivity);

public sealed record AdminWorkspaceMemberDto(
    Guid UserId,
    string FullName,
    string Email,
    WorkspaceRole Role,
    WorkspaceMemberStatus Status,
    bool UserIsActive,
    DateTime JoinedAt);

public sealed record AdminWorkspaceSeriesDto(
    Guid Id,
    string Title,
    bool IsCompleted,
    int ChapterCount,
    int PublishedChapterCount,
    DateTime CreatedAt);

public sealed record AdminWorkspaceAiActivityDto(long Tokens, int Calls, int FailedCalls, int FailedWorkflowRuns);

/// <summary>Healthy = ổn; Degraded = chạy được nhưng chậm/thiếu cấu hình; Down = không gọi được.</summary>
public enum HealthStatus { Healthy = 0, Degraded = 1, Down = 2 }

/// <summary>
/// Một thành phần hạ tầng (Key = database | storage | ai | email): kết quả gọi thử ngay lúc Admin mở trang.
/// Reason khi không Healthy: SLOW | TIMEOUT | UNREACHABLE | SERVICE_KEY_MISSING; email dùng mã EmailFailureReasons
/// (NOT_CONFIGURED, PASSWORD_UNREADABLE = Down; lỗi của lần gửi gần nhất trong 24h = Degraded). QueueDepth chỉ có ở ai.
/// </summary>
public sealed record DependencyHealthDto(
    string Key, HealthStatus Status, string Provider, int? LatencyMs, string? Reason, int? QueueDepth);

/// <summary>Một hàng đợi / việc nền đếm từ DB. Warning = có việc kẹt hoặc lỗi cần Admin xem.</summary>
public sealed record OperationalSignalDto(string Key, int Count, bool Warning, DateTime? OldestAt);

/// <summary>
/// GET /api/admin/system-health (OPS-32). Status chung = mức xấu nhất của các thành phần. Không có job nền:
/// mỗi lần gọi đo lại, nên FE chỉ tự làm mới theo nút bấm hoặc chu kỳ dài.
/// </summary>
public sealed record SystemHealthDto(
    HealthStatus Status,
    DateTime CheckedAt,
    DateTime ServerStartedAt,
    string? Version,
    IReadOnlyList<DependencyHealthDto> Dependencies,
    IReadOnlyList<OperationalSignalDto> Signals);
