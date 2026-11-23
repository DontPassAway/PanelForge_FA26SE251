using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PanelForge.API.Controllers;

public sealed record MobileAppConfigDto(
    string MinVersion,
    string LatestVersion,
    bool MaintenanceMode,
    string MaintenanceMessage,
    string StoreUrlAndroid,
    string StoreUrlIos);

/// <summary>
/// Cấu hình vận hành cho Mobile Reader App (Bảo trì hệ thống & Bắt buộc cập nhật phiên bản mới).
/// </summary>
[ApiController]
[Route("api/app")]
public class AppConfigController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AppConfigController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// GET /api/app/mobile-config
    /// Trả về phiên bản tối thiểu và trạng thái bảo trì để Mobile App hiển thị màn hình Bảo trì / Force Update.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("mobile-config")]
    [ProducesResponseType(typeof(MobileAppConfigDto), StatusCodes.Status200OK)]
    public IActionResult GetMobileConfig()
    {
        var section = _configuration.GetSection("MobileApp");
        var dto = new MobileAppConfigDto(
            MinVersion: section["MinVersion"] ?? "1.4.0",
            LatestVersion: section["LatestVersion"] ?? "1.4.0",
            MaintenanceMode: section.GetValue<bool>("MaintenanceMode"),
            MaintenanceMessage: section["MaintenanceMessage"] ?? "Hệ thống đang bảo trì nâng cấp. Vui lòng quay lại sau ít phút.",
            StoreUrlAndroid: section["StoreUrlAndroid"] ?? "https://play.google.com/store/apps/details?id=com.panelforge.reader",
            StoreUrlIos: section["StoreUrlIos"] ?? string.Empty);

        return Ok(dto);
    }
}
