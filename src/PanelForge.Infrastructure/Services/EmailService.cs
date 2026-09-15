using Microsoft.Extensions.Logging;
using PanelForge.Application.Interfaces;

namespace PanelForge.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("\n====================== [EMAIL NOTIFICATION] ======================");
        _logger.LogInformation("To: {ToEmail}", toEmail);
        _logger.LogInformation("Subject: {Subject}", subject);
        _logger.LogInformation("Body:\n{Body}", htmlBody);
        _logger.LogInformation("===================================================================\n");

        return Task.CompletedTask;
    }

    public Task SendEmailVerificationAsync(string toEmail, string token, string recipientName, CancellationToken cancellationToken = default)
    {
        var subject = "[PanelForge] Xác nhận tài khoản email của bạn";
        var body = $"""
            Xin chào {recipientName},

            Cảm ơn bạn đã đăng ký tài khoản tại PanelForge!
            Mã OTP xác thực email của bạn là: {token}

            Mã này có hiệu lực trong vòng 24 giờ. Vui lòng không chia sẻ mã này cho bất kỳ ai.

            Trân trọng,
            PanelForge Studio Team
            """;

        return SendEmailAsync(toEmail, subject, body, cancellationToken);
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string token, string recipientName, CancellationToken cancellationToken = default)
    {
        var subject = "[PanelForge] Yêu cầu đặt lại mật khẩu";
        var body = $"""
            Xin chào {recipientName},

            Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn tại PanelForge.
            Mã OTP đặt lại mật khẩu của bạn là: {token}

            Mã này có hiệu lực trong vòng 15 phút. Nếu bạn không yêu cầu điều này, xin vui lòng bỏ qua email.

            Trân trọng,
            PanelForge Studio Team
            """;

        return SendEmailAsync(toEmail, subject, body, cancellationToken);
    }
}
