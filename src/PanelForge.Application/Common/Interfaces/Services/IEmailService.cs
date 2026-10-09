namespace PanelForge.Application.Common.Interfaces.Services;

public interface IEmailService
{
    Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
    Task SendEmailVerificationAsync(string toEmail, string token, string recipientName, CancellationToken cancellationToken = default);
    Task SendPasswordResetEmailAsync(string toEmail, string token, string recipientName, CancellationToken cancellationToken = default);
    Task SendChangePasswordOtpAsync(string toEmail, string otp, string recipientName, CancellationToken cancellationToken = default);

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
