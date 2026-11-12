using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Features.Payments.Commands.ApprovePayment;
using PanelForge.Application.Features.Payments.Commands.RejectPayment;
using PanelForge.Application.Features.Payments.Commands.UpdateWorkspaceBankSettings;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Application.Features.Payments.Queries.GetWorkspaceBankSettings;
using PanelForge.Application.Features.Payments.Queries.GetWorkspacePayments;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>
/// API Quản lý Thanh toán VietQR, Phê duyệt giao dịch và Thiết lập Ngân hàng dành cho Producer.
/// </summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/payments")]
[Authorize]
[RequireWorkspaceRole(WorkspaceRole.Producer)]
public class WorkspacePaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public WorkspacePaymentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// GET /api/workspaces/{workspaceId}/payments
    /// Lấy danh sách giao dịch thanh toán VietQR và KPI tổng quan của Studio.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(WorkspacePaymentsResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspacePayments(
        [FromRoute] Guid workspaceId,
        [FromQuery] string? status,
        [FromQuery] Guid? seriesId,
        [FromQuery] string? search,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetWorkspacePaymentsQuery(workspaceId, status, seriesId, search, pageIndex, pageSize),
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.ErrorMessage });
    }

    /// <summary>
    /// POST /api/workspaces/{workspaceId}/payments/{transactionId}/approve
    /// Producer đối soát và duyệt giao dịch VietQR đã nhận tiền.
    /// </summary>
    [HttpPost("{transactionId:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ApprovePayment(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid transactionId,
        CancellationToken cancellationToken)
    {
        var producerUserId = GetCurrentUserId();
        var result = await _mediator.Send(
            new ApprovePaymentCommand(workspaceId, transactionId, producerUserId),
            cancellationToken
        );
        return result.IsSuccess ? Ok(new { message = result.Value }) : BadRequest(new { message = result.ErrorMessage });
    }

    /// <summary>
    /// POST /api/workspaces/{workspaceId}/payments/{transactionId}/reject
    /// Producer từ chối giao dịch kèm lý do.
    /// </summary>
    [HttpPost("{transactionId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectPayment(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid transactionId,
        [FromBody] RejectPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var producerUserId = GetCurrentUserId();
        var result = await _mediator.Send(
            new RejectPaymentCommand(workspaceId, transactionId, producerUserId, request.Reason),
            cancellationToken
        );
        return result.IsSuccess ? Ok(new { message = result.Value }) : BadRequest(new { message = result.ErrorMessage });
    }

    /// <summary>
    /// GET /api/workspaces/{workspaceId}/payments/bank-settings
    /// Lấy thông tin tài khoản ngân hàng thụ hưởng VietQR hiện tại của Studio.
    /// </summary>
    [HttpGet("bank-settings")]
    [ProducesResponseType(typeof(WorkspaceBankSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBankSettings(
        [FromRoute] Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWorkspaceBankSettingsQuery(workspaceId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.ErrorMessage });
    }

    /// <summary>
    /// PUT /api/workspaces/{workspaceId}/payments/bank-settings
    /// Cập nhật thông tin tài khoản ngân hàng thụ hưởng VietQR của Studio.
    /// </summary>
    [HttpPut("bank-settings")]
    [ProducesResponseType(typeof(WorkspaceBankSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateBankSettings(
        [FromRoute] Guid workspaceId,
        [FromBody] UpdateWorkspaceBankSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateWorkspaceBankSettingsCommand(workspaceId, request.BankBin, request.BankName, request.BankAccountNo, request.BankAccountName),
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.ErrorMessage });
    }
}

