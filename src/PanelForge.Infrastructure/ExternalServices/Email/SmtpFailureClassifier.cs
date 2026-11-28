using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Authentication;
using PanelForge.Domain.Entities.Platform;

namespace PanelForge.Infrastructure.ExternalServices.Email;

/// <summary>
/// Đổi exception của SmtpClient thành mã lỗi Admin hiểu được (<see cref="EmailFailureReasons"/>).
/// SmtpClient gói hầu hết lỗi trong SmtpException "Failure sending mail", nên phải xem cả chuỗi InnerException.
/// </summary>
public static class SmtpFailureClassifier
{
    // RFC 4954: 534 cần mật khẩu ứng dụng / cơ chế mạnh hơn, 535 sai thông tin đăng nhập
    private const int AuthMechanismTooWeak = 534;
    private const int AuthCredentialsInvalid = 535;

    public static string Classify(Exception ex)
    {
        if (ex is OperationCanceledException) return EmailFailureReasons.Timeout;
        if (ex is FormatException) return EmailFailureReasons.InvalidAddress;
        if (ex is SmtpFailedRecipientException) return EmailFailureReasons.RecipientRejected;

        var chain = Chain(ex).ToList();
        if (chain.Any(e => e is AuthenticationException)) return EmailFailureReasons.TlsFailed;
        if (chain.Any(e => e is TimeoutException) || ContainsAny(chain, "timed out", "timeout"))
            return EmailFailureReasons.Timeout;
        if (chain.Any(e => e is SocketException)) return EmailFailureReasons.ConnectionFailed;

        if (ex is SmtpException smtp)
        {
            var code = (int)smtp.StatusCode;
            // 530 trên kết nối đã bật TLS nghĩa là máy chủ đòi đăng nhập nhưng chưa nhận được thông tin hợp lệ
            if (code is AuthMechanismTooWeak or AuthCredentialsInvalid
                || smtp.StatusCode is SmtpStatusCode.MustIssueStartTlsFirst or SmtpStatusCode.ClientNotPermitted
                || ContainsAny(chain, "authentication", "username and password", "5.7.8", "5.7.9"))
                return EmailFailureReasons.AuthFailed;

            if (ContainsAny(chain, "secure connection", "starttls", "ssl", "tls"))
                return EmailFailureReasons.TlsFailed;

            if (smtp.StatusCode is SmtpStatusCode.MailboxNameNotAllowed or SmtpStatusCode.MailboxUnavailable
                || ContainsAny(chain, "sender", "5.7.1"))
                return EmailFailureReasons.SenderRejected;
        }

        if (chain.Any(e => e is IOException)) return EmailFailureReasons.ConnectionFailed;
        return EmailFailureReasons.SmtpError;
    }

    /// <summary>Gộp thông báo theo chuỗi InnerException, cắt ngắn. SmtpClient không đưa mật khẩu vào thông báo.</summary>
    public static string Describe(Exception ex)
    {
        var text = string.Join(" → ", Chain(ex).Select(e => e.Message.Trim()).Where(m => m.Length > 0).Distinct());
        return text.Length <= EmailDeliveryLog.MaxDetailLength ? text : text[..EmailDeliveryLog.MaxDetailLength];
    }

    private static IEnumerable<Exception> Chain(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException) yield return e;
    }

    private static bool ContainsAny(IEnumerable<Exception> chain, params string[] needles)
        => chain.Any(e => needles.Any(n => e.Message.Contains(n, StringComparison.OrdinalIgnoreCase)));
}
