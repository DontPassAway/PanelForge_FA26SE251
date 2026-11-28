namespace PanelForge.Application.Common.Interfaces.Services;

public interface IEmailService
{
    /// <returns>false khi không gửi được (thiếu cấu hình SMTP hoặc SMTP lỗi); lỗi đã được ghi log.</returns>
    Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
    Task<bool> SendEmailVerificationAsync(string toEmail, string token, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default);
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string token, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default);
    Task<bool> SendChangePasswordOtpAsync(string toEmail, string otp, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default);

    /// <summary>Gửi email thử (Admin → Cấu hình email) và trả lý do cụ thể khi thất bại.</summary>
    Task<EmailSendResult> SendTestEmailAsync(string toEmail, CancellationToken cancellationToken = default);

    /// <summary>Cấu hình SMTP đang dùng có đủ để gửi không (không kết nối tới máy chủ).</summary>
    Task<EmailConfigurationStatus> GetConfigurationStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Thông báo được thêm vào Studio (UC-01 / UC-03 bước 4).
    /// inviteOtp != null: tài khoản vừa được tạo từ lời mời, người nhận dùng OTP để đặt mật khẩu.
    /// </summary>
    Task SendWorkspaceInvitationAsync(
        string toEmail,
        string recipientName,
        string workspaceName,
        string role,
        string? inviteOtp,
        TimeSpan otpLifetime,
        CancellationToken cancellationToken = default);
}

/// <param name="FailureReason">Mã trong <c>EmailFailureReasons</c> khi thất bại.</param>
/// <param name="Detail">Thông báo gốc của máy chủ SMTP (đã cắt ngắn), không chứa mật khẩu.</param>
public sealed record EmailSendResult(bool Succeeded, string? FailureReason = null, string? Detail = null)
{
    public static readonly EmailSendResult Ok = new(true);
}

/// <param name="Source">app = Admin lưu trong app; config = EmailConfiguration của appsettings / biến môi trường.</param>
/// <param name="Problem">NOT_CONFIGURED | PASSWORD_UNREADABLE khi chưa gửi được.</param>
public sealed record EmailConfigurationStatus(bool IsConfigured, string Source, string? SmtpServer, int? SmtpPort, string? Problem);
