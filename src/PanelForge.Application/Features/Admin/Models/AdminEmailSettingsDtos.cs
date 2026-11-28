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

/// <summary>Một phiên bản cấu hình SMTP trong lịch sử (append-only). Không kèm mật khẩu.</summary>
public sealed record AdminEmailSettingsVersionDto(
    Guid Id,
    string SmtpServer,
    int SmtpPort,
    string SenderName,
    string SenderEmail,
    DateTime ChangedAt,
    string? ChangedByName,
    bool IsCurrent);
