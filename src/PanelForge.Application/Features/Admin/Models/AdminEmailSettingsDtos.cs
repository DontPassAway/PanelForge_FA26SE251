namespace PanelForge.Application.Features.Admin.Models;

/// <summary>
/// Cấu hình SMTP hệ thống cho màn Admin. Không bao giờ trả mật khẩu; <c>HasPassword</c> cho biết đã lưu mật khẩu chưa.
/// <c>IsConfigured = false</c>: chưa lưu trong app, hệ thống đang dùng <c>EmailConfiguration</c> của appsettings / biến môi trường.
/// </summary>
public sealed record AdminEmailSettingsDto(
    bool IsConfigured,
    string? SmtpServer,
    int? SmtpPort,
    string? SenderName,
    string? SenderEmail,
    bool HasPassword,
    DateTime? ChangedAt,
    string? ChangedByName);

/// <summary>Password để trống = giữ mật khẩu đã lưu lần trước (bắt buộc ở lần lưu đầu tiên).</summary>
public sealed record UpdateEmailSettingsRequest(
    string SmtpServer,
    int SmtpPort,
    string SenderName,
    string SenderEmail,
    string? Password);

public sealed record SendTestEmailResponse(string SentTo);

/// <summary>Lý do gửi mail thất bại: Reason là mã trong EmailFailureReasons, Detail là thông báo gốc của SMTP.</summary>
public sealed record EmailDeliveryFailureDto(string Reason, string? Detail);

/// <summary>Một dòng nhật ký gửi email cho Admin (GET /api/admin/email-logs).</summary>
public sealed record EmailDeliveryLogDto(
    Guid Id,
    DateTime CreatedAt,
    string ToEmail,
    string MailType,
    string Subject,
    bool Succeeded,
    string? FailureReason,
    string? ErrorDetail,
    string? SmtpServer,
    int? SmtpPort);

public sealed record EmailDeliveryLogsPageDto(
    IReadOnlyList<EmailDeliveryLogDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int FailedLast24h);
