using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Features.Payments.Commands.ProcessSePayWebhook;

namespace PanelForge.API.Controllers;

/// <summary>
/// Webhook SePay: SePay gọi khi tài khoản ngân hàng đã liên kết có tiền vào, server tự duyệt đơn VietQR khớp mã PFxxxxxx.
/// Cấu hình trên my.sepay.vn: URL <c>https://&lt;domain&gt;/api/payments/sepay/webhook</c>, kiểu chứng thực "API Key",
/// cùng giá trị với <c>SePay:WebhookApiKey</c> (env <c>SePay__WebhookApiKey</c>). SePay gửi header <c>Authorization: Apikey &lt;key&gt;</c>.
/// </summary>
[ApiController]
[Route("api/payments/sepay")]
[AllowAnonymous]
public class SePayWebhookController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public SePayWebhookController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
    }

    /// <summary>POST /api/payments/sepay/webhook — SePay coi 200 + <c>{"success": true}</c> là đã nhận, ngược lại sẽ gửi lại.</summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> Receive([FromBody] SePayWebhookPayload payload, CancellationToken ct)
    {
        var expectedKey = _configuration["SePay:WebhookApiKey"];
        if (string.IsNullOrWhiteSpace(expectedKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "SePay chưa được cấu hình." });

        if (!IsAuthorized(Request.Headers.Authorization.ToString(), expectedKey))
            return Unauthorized(new { success = false });

        var result = await _mediator.Send(new ProcessSePayWebhookCommand(payload), ct);
        return result.IsSuccess
            ? Ok(new { success = true, outcome = result.Value.ToString() })
            : StatusCode(StatusCodes.Status500InternalServerError, new { success = false });
    }

    private static bool IsAuthorized(string header, string expectedKey)
    {
        const string scheme = "Apikey ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return false;
        var given = Encoding.UTF8.GetBytes(header[scheme.Length..].Trim());
        return CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(expectedKey.Trim()));
    }
}
