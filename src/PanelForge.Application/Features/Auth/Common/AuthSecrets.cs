using System.Security.Cryptography;
using System.Text;

namespace PanelForge.Application.Features.Auth.Common;

/// <summary>Sinh / băm các mã bí mật của luồng Auth (chỉ dùng BCL, không phụ thuộc thư viện ngoài).</summary>
internal static class AuthSecrets
{
    public static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RememberDeviceLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>OTP 6 chữ số gửi qua email.</summary>
    public static string NewEmailOtp() => RandomNumberGenerator.GetInt32(100000, 999999).ToString();

    public static string NewRefreshToken() => NewRememberDeviceToken();

    /// <summary>Token ghi nhớ thiết bị: 32 byte ngẫu nhiên, hex 64 ký tự.</summary>
    public static string NewRememberDeviceToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>Chỉ lưu hash SHA-256 của token ghi nhớ thiết bị.</summary>
    public static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>So khớp mã OTP đã lưu, phân biệt hoa thường.</summary>
    public static bool OtpMatches(string? stored, string? provided)
        => !string.IsNullOrEmpty(stored)
           && !string.IsNullOrWhiteSpace(provided)
           && string.Equals(stored, provided.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// OTP đổi mật khẩu dùng chung cột với OTP đặt lại mật khẩu / lời mời; gắn tiền tố khi lưu và khi so khớp để
    /// mã xin cho luồng này không dùng được ở luồng kia.
    /// ponytail: chung một ô lưu nên xin mã luồng này vẫn ghi đè mã đang chờ của luồng kia; tách cột riêng nếu cần cả hai cùng lúc.
    /// </summary>
    public static string ChangePasswordOtp(string otp) => "cp:" + otp.Trim();

    public static bool IsExpired(DateTime? expiresAt) => !expiresAt.HasValue || expiresAt.Value < DateTime.UtcNow;

    /// <summary>Chia secret thành cụm 4 ký tự để người dùng nhập tay.</summary>
    public static string FormatSharedKey(string key)
    {
        var sb = new StringBuilder();
        var position = 0;
        while (position + 4 < key.Length)
        {
            sb.Append(key.AsSpan(position, 4)).Append(' ');
            position += 4;
        }
        if (position < key.Length)
            sb.Append(key.AsSpan(position));
        return sb.ToString().ToLowerInvariant();
    }
}
