using System.Net.Mail;
using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Platform;

/// <summary>
/// Cấu hình SMTP gửi email hệ thống (OTP xác thực, đặt lại mật khẩu, lời mời Studio) do Admin nhập trong app.
/// Append-only: mỗi lần Admin lưu thêm một bản ghi mới, bản ghi mới nhất là cấu hình đang dùng.
/// Chưa có bản ghi nào thì dùng <c>EmailConfiguration</c> trong appsettings / biến môi trường.
/// Mật khẩu SMTP chỉ lưu dạng đã mã hoá (Data Protection), không bao giờ trả ra API.
/// </summary>
public class PlatformEmailSettings : BaseEntity
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    public string SmtpServer { get; private set; } = default!;
    public int SmtpPort { get; private set; }
    public string SenderName { get; private set; } = default!;
    public string SenderEmail { get; private set; } = default!;
    public string ProtectedPassword { get; private set; } = default!;
    public Guid ChangedByUserId { get; private set; }

    private PlatformEmailSettings() { }

    public static PlatformEmailSettings Create(
        string smtpServer, int smtpPort, string senderName, string senderEmail, string protectedPassword, Guid changedByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(smtpServer);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedPassword);

        if (smtpPort is < MinPort or > MaxPort)
            throw new ArgumentException($"Cổng SMTP phải từ {MinPort} đến {MaxPort}.", nameof(smtpPort));

        var server = smtpServer.Trim();
        if (server.Length > 255 || server.Contains(' ') || server.Contains("://"))
            throw new ArgumentException("Máy chủ SMTP chỉ gồm tên miền hoặc địa chỉ IP, ví dụ smtp.gmail.com.", nameof(smtpServer));

        var sender = senderEmail.Trim();
        if (sender.Length > 254 || !MailAddress.TryCreate(sender, out var parsed) || parsed.Address != sender)
            throw new ArgumentException("Email gửi đi không hợp lệ.", nameof(senderEmail));

        var name = senderName.Trim();
        if (name.Length > 100)
            throw new ArgumentException("Tên người gửi tối đa 100 ký tự.", nameof(senderName));

        return new PlatformEmailSettings
        {
            SmtpServer = server,
            SmtpPort = smtpPort,
            SenderName = name,
            SenderEmail = sender,
            ProtectedPassword = protectedPassword,
            ChangedByUserId = changedByUserId
        };
    }
}
