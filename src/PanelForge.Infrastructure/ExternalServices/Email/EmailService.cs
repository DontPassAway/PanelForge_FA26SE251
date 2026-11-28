using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Entities.Platform;

namespace PanelForge.Infrastructure.ExternalServices.Email;

public class EmailService : IEmailService
{
    /// <summary>SendMailAsync không tự dừng theo SmtpClient.Timeout; cổng sai có thể treo rất lâu.</summary>
    internal static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly IPanelForgeDbContext _db;
    private readonly IAiKeyProtector _protector;
    private readonly IServiceScopeFactory _scopeFactory;

    public EmailService(
        IConfiguration config, ILogger<EmailService> logger, IPanelForgeDbContext db,
        IAiKeyProtector protector, IServiceScopeFactory scopeFactory)
    {
        _config = config;
        _logger = logger;
        _db = db;
        _protector = protector;
        _scopeFactory = scopeFactory;
    }

    private sealed record SmtpSettings(
        string Source, string Server, int Port, string SenderName, string? SenderEmail, string? Password, bool PasswordUnreadable);

    /// <summary>Cấu hình Admin lưu trong app (bản ghi mới nhất) được ưu tiên; chưa có thì dùng EmailConfiguration.</summary>
    private async Task<SmtpSettings> ResolveSettingsAsync(CancellationToken ct)
    {
        var saved = await _db.PlatformEmailSettings.AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (saved is not null)
        {
            string? password = null;
            var unreadable = false;
            try
            {
                password = _protector.Unprotect(saved.ProtectedPassword);
            }
            catch (CryptographicException ex)
            {
                // Khoá Data Protection đổi (container dựng lại không giữ DataProtection__KeysPath): Admin cần nhập lại mật khẩu.
                _logger.LogError(ex, "Không giải mã được mật khẩu SMTP đã lưu; Admin cần nhập lại mật khẩu trong Cấu hình email.");
                unreadable = true;
            }
            return new SmtpSettings("app", saved.SmtpServer, saved.SmtpPort, saved.SenderName, saved.SenderEmail, password, unreadable);
        }

        return new SmtpSettings(
            "config",
            _config["EmailConfiguration:SmtpServer"] ?? "smtp.gmail.com",
            int.TryParse(_config["EmailConfiguration:SmtpPort"], out var port) ? port : 587,
            _config["EmailConfiguration:SenderName"] ?? "PanelForge Studio",
            _config["EmailConfiguration:SenderEmail"],
            _config["EmailConfiguration:Password"],
            false);
    }

    private static string? ConfigurationProblem(SmtpSettings s)
        => s.PasswordUnreadable ? EmailFailureReasons.PasswordUnreadable
            : string.IsNullOrWhiteSpace(s.SenderEmail) || string.IsNullOrWhiteSpace(s.Password) ? EmailFailureReasons.NotConfigured
            : null;

    public async Task<EmailConfigurationStatus> GetConfigurationStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        var problem = ConfigurationProblem(settings);
        return new EmailConfigurationStatus(problem is null, settings.Source, settings.Server, settings.Port, problem);
    }

    public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        => (await SendAsync(toEmail, subject, htmlBody, EmailMailTypes.Other, cancellationToken)).Succeeded;

    public Task<EmailSendResult> SendTestEmailAsync(string toEmail, CancellationToken cancellationToken = default)
        => SendAsync(
            toEmail,
            "[PanelForge] Email thử cấu hình SMTP",
            "<p>Đây là email thử từ PanelForge. Cấu hình SMTP của hệ thống đang hoạt động.</p>",
            EmailMailTypes.Test,
            cancellationToken);

    private async Task<EmailSendResult> SendAsync(
        string toEmail, string subject, string htmlBody, string mailType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toEmail);
        ArgumentNullException.ThrowIfNull(subject);

        var settings = await ResolveSettingsAsync(cancellationToken);
        EmailSendResult result;

        if (ConfigurationProblem(settings) is { } problem)
        {
            _logger.LogError("Không gửi được email tới {ToEmail}: chưa cấu hình SMTP ({Problem}).", toEmail, problem);
            result = new EmailSendResult(false, problem);
        }
        else
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendTimeout);
            try
            {
                using var client = new SmtpClient(settings.Server, settings.Port)
                {
                    Credentials = new NetworkCredential(settings.SenderEmail, settings.Password),
                    EnableSsl = true
                };

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(settings.SenderEmail!, settings.SenderName),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(toEmail);

                await client.SendMailAsync(mailMessage, timeout.Token);

                _logger.LogInformation("Đã gửi email {MailType} thành công tới {ToEmail}", mailType, toEmail);
                result = EmailSendResult.Ok;
            }
            catch (Exception ex) when (
                ex is SmtpException or InvalidOperationException or FormatException or IOException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Trả lỗi để handler báo cho người dùng (EMAIL_DELIVERY_FAILED) thay vì báo "đã gửi" sai sự thật.
                var reason = SmtpFailureClassifier.Classify(ex);
                _logger.LogError(ex, "Gửi email {MailType} tới {ToEmail} thất bại ({Reason}): {Message}", mailType, toEmail, reason, ex.Message);
                result = new EmailSendResult(false, reason, SmtpFailureClassifier.Describe(ex));
            }
        }

        await RecordAsync(EmailDeliveryLog.Create(
            toEmail, mailType, subject, result.Succeeded, result.FailureReason, result.Detail, settings.Server, settings.Port));
        return result;
    }

    /// <summary>
    /// Ghi nhật ký bằng DbContext riêng: handler gọi gửi mail thường còn thay đổi chưa lưu trên DbContext của request,
    /// SaveChanges ở đây không được lưu hộ (hoặc làm hỏng) giao dịch đó. Ghi nhật ký lỗi không được chặn việc gửi mail.
    /// </summary>
    private async Task RecordAsync(EmailDeliveryLog log)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IPanelForgeDbContext>();
            db.EmailDeliveryLogs.Add(log);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không ghi được nhật ký gửi email tới {ToEmail}", log.ToEmail);
        }
    }

    public Task<bool> SendEmailVerificationAsync(string toEmail, string token, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default)
    {
        var subject = "[PanelForge] Xác nhận tài khoản email của bạn";
        var body = $@"
            <h3>Xin chào {WebUtility.HtmlEncode(recipientName)},</h3>
            <p>Cảm ơn bạn đã đăng ký tài khoản tại PanelForge!</p>
            <p>Mã OTP xác thực email của bạn là: <strong><span style='font-size: 24px; color: #007bff;'>{token}</span></strong></p>
            <p>Mã này có hiệu lực trong vòng {ValidityText(validFor)}. Vui lòng không chia sẻ mã này cho bất kỳ ai.</p>
            <br/>
            <p>Trân trọng,<br/>PanelForge Studio Team</p>";

        return SendTypedAsync(toEmail, subject, body, EmailMailTypes.Verification, cancellationToken);
    }

    public Task<bool> SendPasswordResetEmailAsync(string toEmail, string token, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default)
    {
        var subject = "[PanelForge] Yêu cầu đặt lại mật khẩu";
        var body = $@"
            <h3>Xin chào {WebUtility.HtmlEncode(recipientName)},</h3>
            <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn tại PanelForge.</p>
            <p>Mã OTP đặt lại mật khẩu của bạn là: <strong><span style='font-size: 24px; color: #dc3545;'>{token}</span></strong></p>
            <p>Mã này có hiệu lực trong vòng {ValidityText(validFor)}. Nếu bạn không yêu cầu điều này, xin vui lòng bỏ qua email.</p>
            <br/>
            <p>Trân trọng,<br/>PanelForge Studio Team</p>";

        return SendTypedAsync(toEmail, subject, body, EmailMailTypes.PasswordReset, cancellationToken);
    }

    public Task<bool> SendChangePasswordOtpAsync(string toEmail, string otp, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default)
    {
        var subject = "[PanelForge] Mã xác thực OTP thay đổi mật khẩu";
        var body = $@"
            <h3>Xin chào {WebUtility.HtmlEncode(recipientName)},</h3>
            <p>Chúng tôi nhận được yêu cầu thay đổi mật khẩu cho tài khoản PanelForge của bạn.</p>
            <p>Mã OTP xác thực thay đổi mật khẩu là: <strong><span style='font-size: 24px; color: #F97316;'>{otp}</span></strong></p>
            <p>Mã này có hiệu lực trong vòng {ValidityText(validFor)}. Tuyệt đối không chia sẻ mã này cho bất kỳ ai.</p>
            <br/>
            <p>Trân trọng,<br/>PanelForge Studio Team</p>";

        return SendTypedAsync(toEmail, subject, body, EmailMailTypes.ChangePasswordOtp, cancellationToken);
    }

    private async Task<bool> SendTypedAsync(string toEmail, string subject, string body, string mailType, CancellationToken ct)
        => (await SendAsync(toEmail, subject, body, mailType, ct)).Succeeded;

    private static string ValidityText(TimeSpan validFor)
        => validFor.TotalMinutes >= 60 && validFor.TotalMinutes % 60 == 0
            ? $"{(int)validFor.TotalHours} giờ"
            : $"{(int)validFor.TotalMinutes} phút";

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

        return SendTypedAsync(toEmail, subject, body, inviteOtp is not null ? EmailMailTypes.WorkspaceInvitation : EmailMailTypes.WorkspaceAdded, cancellationToken);
    }
}