namespace PanelForge.Application.Features.Auth.Common;

/// <summary>
/// Tiện ích nhận diện tên thiết bị và trình duyệt từ chuỗi User-Agent.
/// </summary>
public static class DeviceDetector
{
    public static string DetectDevice(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return "Thiết bị không xác định";

        var ua = userAgent.ToLowerInvariant();
        string os = "Không xác định";
        if (ua.Contains("windows")) os = "Windows";
        else if (ua.Contains("iphone")) os = "iPhone";
        else if (ua.Contains("ipad")) os = "iPad";
        else if (ua.Contains("android")) os = "Android";
        else if (ua.Contains("macintosh") || ua.Contains("mac os")) os = "macOS";
        else if (ua.Contains("linux")) os = "Linux";

        string browser = "Web Browser";
        if (ua.Contains("edg/")) browser = "Edge";
        else if (ua.Contains("chrome/") && !ua.Contains("edg/")) browser = "Chrome";
        else if (ua.Contains("firefox/")) browser = "Firefox";
        else if (ua.Contains("safari/") && !ua.Contains("chrome/")) browser = "Safari";
        else if (ua.Contains("opera/") || ua.Contains("opr/")) browser = "Opera";

        return $"{browser} ({os})";
    }
}
