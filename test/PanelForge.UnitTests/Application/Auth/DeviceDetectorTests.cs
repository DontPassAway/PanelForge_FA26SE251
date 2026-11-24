using FluentAssertions;
using PanelForge.Application.Features.Auth.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Auth;

/// <summary>Tên thiết bị hiển thị ở "Thiết bị đang đăng nhập" (web + app PanelForge).</summary>
public class DeviceDetectorTests
{
    [Theory]
    [InlineData("PanelForge-Mobile/1.0 (Android; Flutter)", "PanelForge App (Android)")]
    [InlineData("PanelForge-Mobile/1.0 (iOS; Flutter)", "PanelForge App (iOS)")]
    [InlineData("PanelForge-Mobile/1.0 (Windows; Flutter)", "PanelForge App (Windows)")]
    [InlineData("PanelForge-Mobile/1.0 (macOS; Flutter)", "PanelForge App (macOS)")]
    [InlineData("PanelForge-Mobile/1.0 (Linux; Flutter)", "PanelForge App (Linux)")]
    [InlineData("panelforge-mobile/2.3", "PanelForge App (Không xác định)")]
    [InlineData("PanelForge-Mobile/1.0 (Fuchsia; Flutter)", "PanelForge App (Không xác định)")]
    public void MobileApp_IsRecognised(string userAgent, string expected) =>
        DeviceDetector.DetectDevice(userAgent).Should().Be(expected);

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36", "Chrome (Windows)")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0", "Edge (Windows)")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", "Safari (iPhone)")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:133.0) Gecko/20100101 Firefox/133.0", "Firefox (Linux)")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36", "Chrome (Android)")]
    public void Browsers_KeepExistingDetection(string userAgent, string expected) =>
        DeviceDetector.DetectDevice(userAgent).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingUserAgent_IsUnknownDevice(string? userAgent) =>
        DeviceDetector.DetectDevice(userAgent).Should().Be("Thiết bị không xác định");
}
