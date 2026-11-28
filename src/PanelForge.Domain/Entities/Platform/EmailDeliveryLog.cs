using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Platform;

/// <summary>
/// Nhật ký mỗi lần hệ thống gửi email (thành công hay thất bại), để Admin biết mail OTP / lời mời có tới không.
/// Append-only. Không lưu nội dung thư vì thư có mã OTP.
/// </summary>
public class EmailDeliveryLog : BaseEntity
{
    public const int MaxDetailLength = 1000;

    public string ToEmail { get; private set; } = default!;
    public string MailType { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public bool Succeeded { get; private set; }
    /// <summary>Mã lỗi trong <see cref="EmailFailureReasons"/>; null khi gửi được.</summary>
    public string? FailureReason { get; private set; }
    /// <summary>Thông báo lỗi gốc của SMTP (đã cắt ngắn), không chứa mật khẩu.</summary>
    public string? ErrorDetail { get; private set; }
    public string? SmtpServer { get; private set; }
    public int? SmtpPort { get; private set; }

    private EmailDeliveryLog() { }

    public static EmailDeliveryLog Create(
        string toEmail, string mailType, string subject, bool succeeded,
        string? failureReason, string? errorDetail, string? smtpServer, int? smtpPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(mailType);

        return new EmailDeliveryLog
        {
            ToEmail = Truncate(toEmail.Trim(), 254)!,
            MailType = Truncate(mailType, 40)!,
            Subject = Truncate(subject ?? string.Empty, 300)!,
            Succeeded = succeeded,
            FailureReason = succeeded ? null : Truncate(failureReason ?? EmailFailureReasons.SmtpError, 40),
            ErrorDetail = succeeded ? null : Truncate(errorDetail, MaxDetailLength),
            SmtpServer = Truncate(smtpServer, 255),
            SmtpPort = smtpPort
        };
    }

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}

/// <summary>Loại thư hệ thống gửi, dùng để lọc nhật ký.</summary>
public static class EmailMailTypes
{
    public const string Verification = "VERIFICATION";
    public const string PasswordReset = "PASSWORD_RESET";
    public const string ChangePasswordOtp = "CHANGE_PASSWORD_OTP";
    public const string WorkspaceInvitation = "WORKSPACE_INVITATION";
    public const string WorkspaceAdded = "WORKSPACE_ADDED";
    public const string Test = "TEST";
    public const string Other = "OTHER";
}

/// <summary>Nhóm lỗi gửi mail để Admin biết phải sửa gì.</summary>
public static class EmailFailureReasons
{
    /// <summary>Chưa có email gửi đi hoặc mật khẩu.</summary>
    public const string NotConfigured = "NOT_CONFIGURED";
    /// <summary>Có mật khẩu đã lưu nhưng không giải mã được (khoá Data Protection đổi).</summary>
    public const string PasswordUnreadable = "PASSWORD_UNREADABLE";
    /// <summary>Máy chủ từ chối đăng nhập: sai email gửi đi / mật khẩu ứng dụng.</summary>
    public const string AuthFailed = "AUTH_FAILED";
    /// <summary>Không kết nối được máy chủ / cổng.</summary>
    public const string ConnectionFailed = "CONNECTION_FAILED";
    /// <summary>Lỗi bắt tay TLS / máy chủ không hỗ trợ STARTTLS trên cổng này.</summary>
    public const string TlsFailed = "TLS_FAILED";
    public const string Timeout = "TIMEOUT";
    /// <summary>Máy chủ từ chối người gửi (email gửi đi không khớp tài khoản).</summary>
    public const string SenderRejected = "SENDER_REJECTED";
    public const string RecipientRejected = "RECIPIENT_REJECTED";
    public const string InvalidAddress = "INVALID_ADDRESS";
    public const string SmtpError = "SMTP_ERROR";
}
