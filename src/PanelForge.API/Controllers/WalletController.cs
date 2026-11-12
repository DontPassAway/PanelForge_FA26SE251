using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Features.Payments.Commands.CreateTopup;
using PanelForge.Application.Features.Payments.Commands.SubscribeVip;
using PanelForge.Application.Features.Payments.Commands.UnlockChapter;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Application.Features.Payments.Queries.GetMyWallet;
using PanelForge.Application.Features.Payments.Queries.GetTransactionStatus;

namespace PanelForge.API.Controllers;

/// <summary>
/// API Quản lý Ví độc giả, Mở khóa chương truyện, Gói VIP và Nạp tiền VietQR cho Mobile App.
/// </summary>
[ApiController]
[Route("api/wallet")]
[Authorize]
public class WalletController : ControllerBase
{
    private readonly IMediator _mediator;

    public WalletController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// GET /api/wallet/me
    /// Lấy thông tin số dư ví, đặc quyền VIP, danh sách chương đã mở khóa và lịch sử giao dịch gần nhất.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserWalletDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWallet(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new GetMyWalletQuery(userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.ErrorMessage });
    }

    /// <summary>
    /// POST /api/wallet/topup
    /// Tạo yêu cầu nạp tiền vào ví qua VietQR. Trả về mã đơn hàng và link mã QR chuẩn Napas.
    /// </summary>
    [HttpPost("topup")]
    [ProducesResponseType(typeof(TopupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTopup([FromBody] TopupRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new CreateTopupCommand(userId, request), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.ErrorMessage });
    }

    /// <summary>
    /// POST /api/wallet/unlock-chapter
    /// Mở khóa chương truyện bằng số dư ví (VNĐ) hoặc đặc quyền VIP.
    /// </summary>
    [HttpPost("unlock-chapter")]
    [ProducesResponseType(typeof(UnlockChapterResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlockChapter([FromBody] UnlockChapterRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new UnlockChapterCommand(userId, request), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.ErrorMessage });
    }

    /// <summary>
    /// POST /api/wallet/subscribe-vip
    /// Kích hoạt gói đọc truyện VIP (tháng / năm) bằng số dư ví.
    /// </summary>
    [HttpPost("subscribe-vip")]
    [ProducesResponseType(typeof(SubscribeVipResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubscribeVip([FromBody] SubscribeVipRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new SubscribeVipCommand(userId, request), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.ErrorMessage });
    }

    /// <summary>
    /// GET /api/wallet/transactions/{orderCode}/status
    /// Mobile polling kiểm tra trạng thái giao dịch VietQR đã được Producer duyệt hay chưa.
    /// </summary>
    [HttpGet("transactions/{orderCode}/status")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TransactionStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionStatus([FromRoute] string orderCode, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTransactionStatusQuery(orderCode), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.ErrorMessage });
    }
}

