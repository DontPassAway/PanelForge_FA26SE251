namespace PanelForge.Application.Common.Interfaces.Services;

public interface IEmailService
{
    /// <returns>false khi không gửi được (thiếu cấu hình SMTP hoặc SMTP lỗi); lỗi đã được ghi log.</returns>
    Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
    Task<bool> SendEmailVerificationAsync(string toEmail, string token, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default);
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string token, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default);
    Task<bool> SendChangePasswordOtpAsync(string toEmail, string otp, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default);

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
