using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.PreviewLinks.Commands;
using PanelForge.Application.Features.PreviewLinks.Models;
using PanelForge.Application.Features.PreviewLinks.Queries;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>
/// CF5 UC-11 / UC-21 / UC-24: link xem thử cho người nhận bên ngoài (không tài khoản) và phản hồi của họ (BR-09, BR-17).
/// Ba đường truy cập tách biệt (BR-24): quản lý link qua quyền Workspace; người nhận chỉ qua token (token đi trong body,
/// không nằm trên URL để không lọt vào log request); ảnh qua link ký ngắn hạn.
/// </summary>
[ApiController]
[Authorize]
public sealed class PreviewLinksController : ControllerBase
{
    private readonly IMediator _mediator;

    public PreviewLinksController(IMediator mediator) => _mediator = mediator;

    private Guid GetCurrentUserId() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    // ── Phía Studio ──

    /// <summary>POST /api/chapters/{chapterId}/preview-links — Producer tạo link (chương Approved). Token chỉ trả một lần.</summary>
    [HttpPost("api/chapters/{chapterId:guid}/preview-links")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(CreatedPreviewLinkDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid chapterId, [FromBody] CreatePreviewLinkRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreatePreviewLinkCommand(chapterId, GetCurrentUserId(), body.RecipientEmail, body.ExpiresInHours), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Error(result);
    }

    /// <summary>GET /api/chapters/{chapterId}/preview-links — danh sách link + thống kê truy cập (Producer, Editor).</summary>
    [HttpGet("api/chapters/{chapterId:guid}/preview-links")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(IReadOnlyList<PreviewLinkDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid chapterId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPreviewLinksQuery(chapterId), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST /api/chapters/{chapterId}/preview-links/{linkId}/revoke — Producer thu hồi trước hạn.</summary>
    [HttpPost("api/chapters/{chapterId:guid}/preview-links/{linkId:guid}/revoke")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid chapterId, Guid linkId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokePreviewLinkCommand(chapterId, linkId, GetCurrentUserId()), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    /// <summary>GET /api/chapters/{chapterId}/preview-feedback?status= — phản hồi từ link xem thử (Producer, Editor).</summary>
    [HttpGet("api/chapters/{chapterId:guid}/preview-feedback")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(IReadOnlyList<PreviewFeedbackDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Feedback(Guid chapterId, [FromQuery] PreviewFeedbackStatus? status, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPreviewFeedbackQuery(chapterId, status), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST /api/chapters/{chapterId}/preview-feedback/{feedbackId}/triage — Noted | NeedsChange (note bắt buộc).</summary>
    [HttpPost("api/chapters/{chapterId:guid}/preview-feedback/{feedbackId:guid}/triage")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(PreviewFeedbackDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Triage(
        Guid chapterId, Guid feedbackId, [FromBody] TriagePreviewFeedbackRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new TriagePreviewFeedbackCommand(chapterId, feedbackId, GetCurrentUserId(), body.Decision, body.Note), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/preview-feedback/{feedbackId}/change-request — phản hồi NeedsChange thành ChangeRequest
    /// của một việc (chương phải đã mở lại). Việc đã duyệt bị trả về làm lại.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/preview-feedback/{feedbackId:guid}/change-request")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(PreviewFeedbackDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateChangeRequest(
        Guid chapterId, Guid feedbackId, [FromBody] CreateChangeRequestFromFeedbackRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateChangeRequestFromFeedbackCommand(
            chapterId, feedbackId, GetCurrentUserId(), body.AssignmentId, body.Description, body.DueDate), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Error(result);
    }

    // ── Phía người nhận (không tài khoản) ──

    /// <summary>POST /api/preview/open — body { token }. 403 nếu token sai / hết hạn / bị thu hồi (đều ghi log).</summary>
    [HttpPost("api/preview/open")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PreviewChapterDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Open([FromBody] PreviewTokenRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new OpenPreviewCommand(body.Token, Ip), ct);
        Response.Headers.CacheControl = "no-store";
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>GET /api/preview/images?token= — ảnh trang (token ảnh ngắn hạn lấy từ ImageUrl của /open).</summary>
    [HttpGet("api/preview/images")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Image([FromQuery] string? token, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPreviewImageQuery(token, Ip), ct);
        if (!result.IsSuccess) return Error(result);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    /// <summary>POST /api/preview/feedback — body { token, message, pageId?, panelId? }.</summary>
    [HttpPost("api/preview/feedback")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PreviewFeedbackDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> SubmitFeedback([FromBody] SubmitPreviewFeedbackRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new SubmitPreviewFeedbackCommand(body.Token, body.Message, body.PageId, body.PanelId, Ip, body.ReviewerName), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Error(result);
    }

    private IActionResult Error<T>(Result<T> result) => result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
        ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
        ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage, code = result.ErrorCode }),
        _ => BadRequest(new { error = result.ErrorMessage })
    };
}
