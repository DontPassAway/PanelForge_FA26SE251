using PanelForge.Application.Common;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Features.Payments.Commands.AdminRefundWallet;
using PanelForge.Application.Features.Payments.Commands.CreateTopup;
using PanelForge.Application.Features.Payments.Commands.ReportTransactionIssue;
using PanelForge.Application.Features.Payments.Commands.UnlockChapter;
using PanelForge.Application.Features.Payments.Commands.VerifyGooglePlayPurchase;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Application.Features.Payments.Queries.GetMyWallet;
using PanelForge.Application.Features.Payments.Queries.GetTransactionStatus;

namespace PanelForge.API.Controllers;

/// <summary>
/// API Quản lý Ví độc giả, Mở khóa chương truyện và Nạp tiền VietQR cho Mobile App.
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
    /// Lấy thông tin số dư ví, danh sách chương đã mở khóa và lịch sử giao dịch gần nhất.
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
        if (result.IsSuccess) return Ok(result.Value);
        return result.ErrorCode switch
        {
            ResultErrorCodes.NotFound or ResultErrorCodes.EditionNotPublished =>
                NotFound(new { message = result.ErrorMessage, code = result.ErrorCode }),
            ResultErrorCodes.Conflict or ResultErrorCodes.PriceChanged => Conflict(new { message = result.ErrorMessage, code = result.ErrorCode }),
            _ => BadRequest(new { message = result.ErrorMessage }),
        };
    }

    /// <summary>
    /// POST /api/wallet/unlock-chapter
    /// Mở khóa chương truyện bằng số dư ví (VNĐ).
    /// </summary>
    [HttpPost("unlock-chapter")]
    [ProducesResponseType(typeof(UnlockChapterResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlockChapter([FromBody] UnlockChapterRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new UnlockChapterCommand(userId, request), cancellationToken);
        if (result.IsSuccess) return Ok(result.Value);
        return result.ErrorCode switch
        {
            ResultErrorCodes.NotFound or ResultErrorCodes.EditionNotPublished =>
                NotFound(new { message = result.ErrorMessage, code = result.ErrorCode }),
            ResultErrorCodes.Conflict or ResultErrorCodes.PriceChanged => Conflict(new { message = result.ErrorMessage, code = result.ErrorCode }),
            _ => BadRequest(new { message = result.ErrorMessage }),
        };
    }

    /// <summary>
    /// GET /api/wallet/transactions/{orderCode}/status
    /// Mobile polling kiểm tra trạng thái giao dịch VietQR đã được duyệt hay chưa (yêu cầu đăng nhập; chỉ chủ đơn hoặc Publisher/Admin).
    /// </summary>
    [HttpGet("transactions/{orderCode}/status")]
    [ProducesResponseType(typeof(TransactionStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionStatus([FromRoute] string orderCode, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var isPrivileged = User.IsInRole("Publisher") || User.IsInRole("Admin");
        var result = await _mediator.Send(new GetTransactionStatusQuery(orderCode, userId, isPrivileged), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.ErrorMessage });
    }

    /// <summary>
    /// POST /api/wallet/google-play/verify
    /// Xác thực giao dịch Google Play Billing (In-App Purchase) trên Android CH Play và cộng tiền vào Ví (có chống cộng trùng).
    /// </summary>
    [HttpPost("google-play/verify")]
    [ProducesResponseType(typeof(VerifyGooglePlayPurchaseResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyGooglePlayPurchase(
        [FromBody] VerifyGooglePlayPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(
            new VerifyGooglePlayPurchaseCommand(userId, request.ProductId, request.PurchaseToken, request.PackageName),
            cancellationToken);
        if (result.IsSuccess) return Ok(result.Value);
        return result.ErrorCode switch
        {
            ResultErrorCodes.PaymentProviderUnavailable =>
                StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = result.ErrorMessage }),
            ResultErrorCodes.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => BadRequest(new { message = result.ErrorMessage }),
        };
    }

    /// <summary>
    /// POST /api/wallet/transactions/{orderCode}/dispute
    /// Độc giả gửi báo cáo lỗi giao dịch để Admin kiểm tra đối soát.
    /// </summary>
    [HttpPost("transactions/{orderCode}/dispute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReportTransactionIssue(
        [FromRoute] string orderCode,
        [FromBody] ReportTransactionIssueRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(
            new ReportTransactionIssueCommand(userId, orderCode, request.Reason),
            cancellationToken);
        return result.IsSuccess ? Ok(new { message = result.Value }) : BadRequest(new { message = result.ErrorMessage });
    }

    /// <summary>
    /// POST /api/wallet/admin/refund
    /// Chỉ Quản trị viên (Admin) được hoàn tiền vào Ví độc giả khi xảy ra sự cố kỹ thuật.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("admin/refund")]
    [ProducesResponseType(typeof(AdminRefundWalletResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdminRefund(
        [FromBody] AdminRefundWalletRequest request,
        CancellationToken cancellationToken)
    {
        var adminUserId = GetCurrentUserId();
        if (adminUserId == Guid.Empty) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(
            new AdminRefundWalletCommand(adminUserId, request.TargetUserId, request.AmountVnd, request.Reason, request.ReferenceOrderCode),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.ErrorMessage });
    }
}

