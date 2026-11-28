using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Features.Payments.Commands.ApprovePayment;
using PanelForge.Application.Features.Payments.Commands.RejectPayment;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Application.Features.Payments.Queries.GetWorkspacePayments;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>
/// API giao dịch VietQR/Ví của một Studio — Publisher/Admin. Tài khoản ngân hàng nhận tiền là của nền tảng (Q13): xem PlatformPaymentsController.
/// Producer không còn xem/quản lý doanh thu; Producer xem lượt đọc qua GET /api/workspaces/{id}/reader-stats.
/// </summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/payments")]
[Authorize]
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
    /// Lấy danh sách giao dịch thanh toán VietQR và KPI tổng quan của Studio (Publisher &amp; Admin chỉ đọc — BR-32).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Publisher,Admin")]
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
    /// Chỉ Publisher đối soát và duyệt giao dịch VietQR đã nhận tiền (Q13 / BR-33).
    /// </summary>
    [HttpPost("{transactionId:guid}/approve")]
    [Authorize(Roles = "Publisher")]
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
    /// Chỉ Publisher từ chối giao dịch kèm lý do (Q13 / BR-33).
    /// </summary>
    [HttpPost("{transactionId:guid}/reject")]
    [Authorize(Roles = "Publisher")]
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
}
