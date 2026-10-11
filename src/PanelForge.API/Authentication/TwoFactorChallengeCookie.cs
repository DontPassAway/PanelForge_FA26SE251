using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace PanelForge.API.Authentication;

/// <summary>
/// Cookie HttpOnly ngắn hạn chứng nhận người gọi vừa qua bước mật khẩu (login / external-login) của tài khoản bật 2FA.
/// verify-2fa chỉ hoàn tất đăng nhập khi có cookie này, nên mã TOTP không thể thay cho mật khẩu.
/// Giá trị là email được mã hoá + ký bằng Data Protection, tự hết hạn sau 5 phút; không lưu DB.
/// </summary>
public static class TwoFactorChallengeCookie
{
    public const string Name = "pf_2fa";
    private const string Path = "/api/auth";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static void Set(HttpContext context, string email)
        => context.Response.Cookies.Append(Name, Protector(context).Protect(email, Lifetime), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = Path,
            MaxAge = Lifetime
        });

    /// <summary>Email của thử thách 2FA đang mở; null nếu không có cookie, cookie bị sửa hoặc đã quá 5 phút.</summary>
    public static string? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(Name, out var value) || string.IsNullOrEmpty(value))
            return null;

        try
        {
            return Protector(context).Unprotect(value);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static void Clear(HttpContext context)
        => context.Response.Cookies.Delete(Name, new CookieOptions { Path = Path });

    private static ITimeLimitedDataProtector Protector(HttpContext context)
        => context.RequestServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PanelForge.TwoFactorChallenge")
            .ToTimeLimitedDataProtector();
}
