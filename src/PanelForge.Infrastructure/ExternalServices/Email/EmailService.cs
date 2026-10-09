using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.ExternalServices.Email;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toEmail);
        ArgumentNullException.ThrowIfNull(subject);

        // Lấy cấu hình từ appsettings.json
        var senderName = _config["EmailConfiguration:SenderName"] ?? "PanelForge Studio";
        var senderEmail = _config["EmailConfiguration:SenderEmail"] ?? throw new ArgumentNullException("SenderEmail configuration is missing.");
        var password = _config["EmailConfiguration:Password"] ?? throw new ArgumentNullException("Password configuration is missing.");
        var smtpServer = _config["EmailConfiguration:SmtpServer"] ?? "smtp.gmail.com";
        var smtpPortString = _config["EmailConfiguration:SmtpPort"];
        int smtpPort = int.TryParse(smtpPortString, out var port) ? port : 587;

        try
        {
            using var client = new SmtpClient(smtpServer, smtpPort)
            {
                Credentials = new NetworkCredential(senderEmail, password),
                EnableSsl = true
            };

            using var mailMessage = new MailMessage
            {
                From = new MailAddress(senderEmail, senderName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            mailMessage.To.Add(toEmail);

            await client.SendMailAsync(mailMessage, cancellationToken);

            _logger.LogInformation("Đã gửi email thành công tới {ToEmail}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception xảy ra khi gửi email qua SMTP: {Message}", ex.Message);
        }
    }

    public Task SendEmailVerificationAsync(string toEmail, string token, string recipientName, CancellationToken cancellationToken = default)
    {
        var subject = "[PanelForge] Xác nhận tài khoản email của bạn";
        var body = $@"
            <h3>Xin chào {recipientName},</h3>
            <p>Cảm ơn bạn đã đăng ký tài khoản tại PanelForge!</p>
            <p>Mã OTP xác thực email của bạn là: <strong><span style='font-size: 24px; color: #007bff;'>{token}</span></strong></p>
            <p>Mã này có hiệu lực trong vòng 24 giờ. Vui lòng không chia sẻ mã này cho bất kỳ ai.</p>
            <br/>
            <p>Trân trọng,<br/>PanelForge Studio Team</p>";

        return SendEmailAsync(toEmail, subject, body, cancellationToken);
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string token, string recipientName, CancellationToken cancellationToken = default)
    {
        var subject = "[PanelForge] Yêu cầu đặt lại mật khẩu";
        var body = $@"
            <h3>Xin chào {recipientName},</h3>
            <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn tại PanelForge.</p>
            <p>Mã OTP đặt lại mật khẩu của bạn là: <strong><span style='font-size: 24px; color: #dc3545;'>{token}</span></strong></p>
            <p>Mã này có hiệu lực trong vòng 15 phút. Nếu bạn không yêu cầu điều này, xin vui lòng bỏ qua email.</p>
            <br/>
            <p>Trân trọng,<br/>PanelForge Studio Team</p>";

        return SendEmailAsync(toEmail, subject, body, cancellationToken);
    }

    public Task SendChangePasswordOtpAsync(string toEmail, string otp, string recipientName, CancellationToken cancellationToken = default)
    {
        var subject = "[PanelForge] Mã xác thực OTP thay đổi mật khẩu";
        var body = $@"
            <h3>Xin chào {recipientName},</h3>
            <p>Chúng tôi nhận được yêu cầu thay đổi mật khẩu cho tài khoản PanelForge của bạn.</p>
            <p>Mã OTP xác thực thay đổi mật khẩu là: <strong><span style='font-size: 24px; color: #F97316;'>{otp}</span></strong></p>
            <p>Mã này có hiệu lực trong vòng 15 phút. Tuyệt đối không chia sẻ mã này cho bất kỳ ai.</p>
            <br/>
            <p>Trân trọng,<br/>PanelForge Studio Team</p>";

        return SendEmailAsync(toEmail, subject, body, cancellationToken);
    }

    public Task SendWorkspaceInvitationAsync(
        string toEmail,
        string recipientName,
        string workspaceName,
        string role,
        string? inviteOtp,
        TimeSpan otpLifetime,
        CancellationToken cancellationToken = default)
    {
        var safeName = WebUtility.HtmlEncode(recipientName);
        var safeWorkspace = WebUtility.HtmlEncode(workspaceName);

        string subject;
        string body;
        if (inviteOtp is not null)
        {
            subject = $"[PanelForge] Bạn được mời tham gia Studio {workspaceName}";
            body = $@"
            <h3>Xin chào {safeName},</h3>
            <p>Bạn được mời tham gia Studio <strong>{safeWorkspace}</strong> trên PanelForge với vai trò <strong>{role}</strong>.</p>
            <p>Một tài khoản đã được tạo cho email này. Để kích hoạt, hãy mở trang <em>Đặt lại mật khẩu</em> và nhập mã OTP:</p>
            <p><strong><span style='font-size: 24px; color: #007bff;'>{inviteOtp}</span></strong></p>
            <p>Mã có hiệu lực trong {(int)otpLifetime.TotalMinutes} phút. Nếu mã hết hạn, hãy dùng chức năng <em>Quên mật khẩu</em> với email này để nhận mã mới.</p>
            <p>Nếu bạn không mong đợi lời mời này, hãy bỏ qua email.</p>
            <br/>
            <p>Trân trọng,<br/>PanelForge Studio Team</p>";
        }
        else
        {
            subject = $"[PanelForge] Bạn đã được thêm vào Studio {workspaceName}";
            body = $@"
            <h3>Xin chào {safeName},</h3>
            <p>Bạn đã được thêm vào Studio <strong>{safeWorkspace}</strong> trên PanelForge với vai trò <strong>{role}</strong>.</p>
            <p>Đăng nhập để xem Studio trong mục <em>Your Workspaces</em>.</p>
            <br/>
            <p>Trân trọng,<br/>PanelForge Studio Team</p>";
        }

        return SendEmailAsync(toEmail, subject, body, cancellationToken);
    }
}