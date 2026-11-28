using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Payments.Commands.ApprovePayment;
using PanelForge.Application.Features.Payments.Commands.RejectPayment;
using PanelForge.Application.Features.Payments.Commands.UpdatePlatformBankAccount;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Application.Features.Payments.Queries.GetPlatformBankAccount;
using PanelForge.Application.Features.Payments.Queries.GetWorkspacePayments;

namespace PanelForge.API.Controllers;

/// <summary>
/// Thanh toán ở cấp nền tảng (Q13, BR-32): tài khoản ngân hàng nền tảng nhận mọi tiền VietQR, đối soát và duyệt giao dịch
/// của mọi Studio (kể cả đơn nạp Ví không thuộc Studio nào). Ghi: chỉ Publisher. Đọc: Publisher và Admin.
/// </summary>
[ApiController]
[Route("api/platform/payments")]
[Authorize]
public class PlatformPaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformPaymentsController(IMediator mediator) => _mediator = mediator;

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>GET /api/platform/payments — KPI và danh sách giao dịch toàn nền tảng.</summary>
    [HttpGet]
    [Authorize(Roles = "Publisher,Admin")]
    [ProducesResponseType(typeof(WorkspacePaymentsResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayments(
        [FromQuery] string? status,
        [FromQuery] Guid? seriesId,
        [FromQuery] string? search,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => ToActionResult(await _mediator.Send(
            new GetWorkspacePaymentsQuery(null, status, seriesId, search, pageIndex, pageSize), cancellationToken));

    /// <summary>POST /api/platform/payments/{transactionId}/approve — Publisher duyệt tay giao dịch VietQR đã nhận tiền.</summary>
    [HttpPost("{transactionId:guid}/approve")]
    [Authorize(Roles = "Publisher")]
    public async Task<IActionResult> Approve([FromRoute] Guid transactionId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ApprovePaymentCommand(null, transactionId, GetCurrentUserId()), cancellationToken);
        return result.IsSuccess ? Ok(new { message = result.Value }) : ToError(result);
    }

    /// <summary>POST /api/platform/payments/{transactionId}/reject — Publisher từ chối giao dịch kèm lý do.</summary>
    [HttpPost("{transactionId:guid}/reject")]
    [Authorize(Roles = "Publisher")]
    public async Task<IActionResult> Reject(
        [FromRoute] Guid transactionId,
        [FromBody] RejectPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RejectPaymentCommand(null, transactionId, GetCurrentUserId(), request.Reason), cancellationToken);
        return result.IsSuccess ? Ok(new { message = result.Value }) : ToError(result);
    }

    /// <summary>GET /api/platform/payments/bank-account — tài khoản nhận tiền đang dùng và lịch sử thay đổi.</summary>
    [HttpGet("bank-account")]
    [Authorize(Roles = "Publisher,Admin")]
    [ProducesResponseType(typeof(PlatformBankAccountResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBankAccount(CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(new GetPlatformBankAccountQuery(), cancellationToken));

    /// <summary>PUT /api/platform/payments/bank-account — Publisher đổi tài khoản nhận tiền, bắt buộc mã 2FA.</summary>
    [HttpPut("bank-account")]
    [Authorize(Roles = "Publisher")]
    [ProducesResponseType(typeof(PlatformBankAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateBankAccount(
        [FromBody] UpdatePlatformBankAccountRequest request,
        CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(new UpdatePlatformBankAccountCommand(
            GetCurrentUserId(), request.BankBin, request.BankName, request.AccountNo, request.AccountName, request.TwoFactorCode),
            cancellationToken));

    private IActionResult ToActionResult<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : ToError(result);

    private IActionResult ToError<T>(Result<T> result) => result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { message = result.ErrorMessage, code = result.ErrorCode }),
        ResultErrorCodes.Unauthorized => Unauthorized(new { message = result.ErrorMessage, code = result.ErrorCode }),
        ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { message = result.ErrorMessage, code = result.ErrorCode }),
        _ => BadRequest(new { message = result.ErrorMessage, code = result.ErrorCode })
    };
}
