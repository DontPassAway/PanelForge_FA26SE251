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

    // Path cookie phân biệt hoa thường: "/api/auth" không khớp route thật "/api/Auth/refresh" → trình duyệt không gửi
    // cookie, refresh luôn 401 và người dùng bị logout khi access token hết hạn. Dùng "/api" để không phụ thuộc cách viết.
    private const string RefreshPath = "/api";
    private const string LegacyRefreshPath = "/api/auth";
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

    public static void Clear(HttpResponse response, HttpRequest? request = null)
    {
        var isHttps = request?.IsHttps ?? false;
        var sameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax;
        var deleteOptApi = new CookieOptions { Path = "/api", HttpOnly = true, Secure = isHttps, SameSite = sameSite };
        var deleteOptRefresh = new CookieOptions { Path = RefreshPath, HttpOnly = true, Secure = isHttps, SameSite = sameSite };
        var deleteOptLegacy = new CookieOptions { Path = LegacyRefreshPath, HttpOnly = true, Secure = isHttps, SameSite = sameSite };

        response.Cookies.Delete(Access, deleteOptApi);
        response.Cookies.Delete(Refresh, deleteOptRefresh);
        response.Cookies.Delete(Refresh, deleteOptLegacy); // cookie cũ trước bản sửa path
    }

    // Header tuỳ chỉnh (trang khác không tự gắn được), hoặc request cùng origin – vd. Swagger UI trên chính API
    // (Sec-Fetch-Site do trình duyệt đặt, JS không sửa được).
    public static bool HasCsrfHeader(HttpRequest request)
        => !string.IsNullOrEmpty(request.Headers[CsrfHeader])
           || string.Equals(request.Headers["Sec-Fetch-Site"], "same-origin", StringComparison.OrdinalIgnoreCase);

    private static CookieOptions Options(HttpRequest request, string path, DateTimeOffset? expires)
    {
        var isHttps = request.IsHttps;
        return new()
        {
            HttpOnly = true,
            Secure = isHttps, // dev chạy http://localhost
            // Cross-site (FE và BE khác domain) yêu cầu SameSite=None + Secure=true trên HTTPS.
            // Chạy local HTTP fallback SameSite=Lax vì trình duyệt từ chối SameSite=None nếu không Secure.
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = path,
            Expires = expires
        };
    }
}
