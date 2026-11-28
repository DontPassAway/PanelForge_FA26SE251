using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common.Email;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.ExternalServices.Email;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly IPanelForgeDbContext _db;
    private readonly IAiKeyProtector _protector;

    public EmailService(IConfiguration config, ILogger<EmailService> logger, IPanelForgeDbContext db, IAiKeyProtector protector)
    {
        _config = config;
        _logger = logger;
        _db = db;
        _protector = protector;
    }

    private sealed record SmtpSettings(string Server, int Port, string SenderName, string? SenderEmail, string? Password);

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
            try
            {
                password = _protector.Unprotect(saved.ProtectedPassword);
            }
            catch (CryptographicException ex)
            {
                // Khoá Data Protection đổi (container dựng lại không giữ DataProtection__KeysPath): Admin cần nhập lại mật khẩu.
                _logger.LogError(ex, "Không giải mã được mật khẩu SMTP đã lưu; Admin cần nhập lại mật khẩu trong Cấu hình email.");
            }
            return new SmtpSettings(saved.SmtpServer, saved.SmtpPort, saved.SenderName, saved.SenderEmail, password);
        }

        return new SmtpSettings(
            _config["EmailConfiguration:SmtpServer"] ?? "smtp.gmail.com",
            int.TryParse(_config["EmailConfiguration:SmtpPort"], out var port) ? port : 587,
            _config["EmailConfiguration:SenderName"] ?? "PanelForge Studio",
            _config["EmailConfiguration:SenderEmail"],
            _config["EmailConfiguration:Password"]);
    }

    public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toEmail);
        ArgumentNullException.ThrowIfNull(subject);

        var (smtpServer, smtpPort, senderName, senderEmail, password) = await ResolveSettingsAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogError("Không gửi được email tới {ToEmail}: chưa cấu hình SMTP (Admin → Cấu hình email, hoặc EmailConfiguration).", toEmail);
            return false;
        }

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
            return true;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException or IOException)
        {
            // Trả false để handler báo lỗi cho người dùng (EMAIL_DELIVERY_FAILED) thay vì báo "đã gửi" sai sự thật.
            _logger.LogError(ex, "Exception xảy ra khi gửi email qua SMTP: {Message}", ex.Message);
            return false;
        }
    }

    public Task<bool> SendEmailVerificationAsync(string toEmail, string token, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default)
        => SendTemplateAsync(toEmail, EmailTemplateCatalog.EmailVerification,
            new() { ["name"] = recipientName, ["code"] = token, ["validFor"] = ValidityText(validFor) }, cancellationToken);

    public Task<bool> SendPasswordResetEmailAsync(string toEmail, string token, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default)
        => SendTemplateAsync(toEmail, EmailTemplateCatalog.PasswordReset,
            new() { ["name"] = recipientName, ["code"] = token, ["validFor"] = ValidityText(validFor) }, cancellationToken);

    public Task<bool> SendChangePasswordOtpAsync(string toEmail, string otp, string recipientName, TimeSpan validFor, CancellationToken cancellationToken = default)
        => SendTemplateAsync(toEmail, EmailTemplateCatalog.ChangePasswordOtp,
            new() { ["name"] = recipientName, ["code"] = otp, ["validFor"] = ValidityText(validFor) }, cancellationToken);

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
        var values = new Dictionary<string, string>
        {
            ["name"] = recipientName,
            ["studio"] = workspaceName,
            ["role"] = role
        };
        if (inviteOtp is not null)
        {
            values["code"] = inviteOtp;
            values["validFor"] = ValidityText(otpLifetime);
        }

        return SendTemplateAsync(
            toEmail,
            inviteOtp is not null ? EmailTemplateCatalog.StudioInviteNewAccount : EmailTemplateCatalog.StudioInviteExistingAccount,
            values,
            cancellationToken);
    }

    /// <summary>Nội dung Admin sửa trong app (bản mới nhất của loại email) được ưu tiên; chưa sửa thì dùng nội dung mặc định.</summary>
    private async Task<bool> SendTemplateAsync(
        string toEmail, string templateKey, Dictionary<string, string> values, CancellationToken ct)
    {
        var definition = EmailTemplateCatalog.Find(templateKey)
                         ?? throw new InvalidOperationException($"Chưa khai báo mẫu email '{templateKey}'.");

        var saved = await _db.PlatformEmailTemplates.AsNoTracking()
            .Where(t => t.TemplateKey == templateKey && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new EmailTemplateContent(t.Subject, t.Greeting, t.Intro, t.Note, t.Signature))
            .FirstOrDefaultAsync(ct);

        var (subject, body) = EmailTemplateCatalog.Render(definition, saved ?? definition.Default, values);
        return await SendEmailAsync(toEmail, subject, body, ct);
    }
}