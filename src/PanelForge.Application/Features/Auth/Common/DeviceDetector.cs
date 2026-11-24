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

        var ua = userAgent.Trim().ToLowerInvariant();

        // App PanelForge gửi "PanelForge-Mobile/1.0 (<Android|iOS|Windows|macOS|Linux>; Flutter)".
        if (ua.StartsWith("panelforge-mobile", StringComparison.Ordinal))
            return $"PanelForge App ({DetectAppPlatform(ua)})";

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

    private static string DetectAppPlatform(string ua)
    {
        var open = ua.IndexOf('(');
        var platform = open >= 0 ? ua[(open + 1)..].Split(';', ')')[0].Trim() : string.Empty;
        return platform switch
        {
            "android" => "Android",
            "ios" => "iOS",
            "windows" => "Windows",
            "macos" => "macOS",
            "linux" => "Linux",
            _ => "Không xác định"
        };
    }
}
