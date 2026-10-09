using PanelForge.Application.Features.Auth.Models;

namespace PanelForge.API.Authentication;

/// <summary>
/// Token đăng nhập trong cookie HttpOnly (JS không đọc được → XSS không lấy cắp được token).
/// Chống CSRF: SameSite=Lax + request dùng cookie mà đổi dữ liệu phải có header <see cref="CsrfHeader"/>.
/// </summary>
public static class AuthCookies
{
    public const string Access = "pf_access";
    public const string Refresh = "pf_refresh";
    public const string CsrfHeader = "X-Requested-With";

    private const string RefreshPath = "/api/auth";
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30); // = thời hạn refresh token ở BE

    public static void Set(HttpRequest request, HttpResponse response, AuthResponse auth)
    {
        if (!string.IsNullOrEmpty(auth.Token))
            response.Cookies.Append(Access, auth.Token, Options(request, "/api",
                auth.ExpiresAt is { } exp ? new DateTimeOffset(DateTime.SpecifyKind(exp, DateTimeKind.Utc)) : null));

        if (!string.IsNullOrEmpty(auth.RefreshToken))
            response.Cookies.Append(Refresh, auth.RefreshToken, Options(request, RefreshPath,
                DateTimeOffset.UtcNow.Add(RefreshLifetime)));
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Access, new CookieOptions { Path = "/api" });
        response.Cookies.Delete(Refresh, new CookieOptions { Path = RefreshPath });
    }

    public static bool HasCsrfHeader(HttpRequest request)
        => !string.IsNullOrEmpty(request.Headers[CsrfHeader]);

    private static CookieOptions Options(HttpRequest request, string path, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps, // dev chạy http://localhost
        SameSite = SameSiteMode.Lax,
        Path = path,
        Expires = expires
    };
}
