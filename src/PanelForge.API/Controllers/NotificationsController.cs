using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Notifications.Commands.MarkNotificationsRead;
using PanelForge.Application.Features.Notifications.Commands.SendAssignmentReminders;
using PanelForge.Application.Features.Notifications.Queries.GetMyNotifications;

namespace PanelForge.API.Controllers;

/// <summary>Thông báo trong ứng dụng (UC-14). Chỉ làm việc trên thông báo của chính người gọi nên không cần quyền theo Workspace.</summary>
[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator) => _mediator = mediator;

    private Guid GetCurrentUserId() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    /// <summary>GET /api/notifications?unreadOnly=&amp;pageIndex=1&amp;pageSize=20 (pageSize tối đa 50).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NotificationListDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(
        [FromQuery] bool unreadOnly = false, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyNotificationsQuery(GetCurrentUserId(), unreadOnly, pageIndex, pageSize), ct);
        return Ok(result.Value);
    }

    /// <summary>POST /api/notifications/{id}/read — đánh dấu một thông báo đã đọc.</summary>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new MarkNotificationsReadCommand(GetCurrentUserId(), id), ct);
        return result.IsSuccess ? Ok(new { marked = result.Value }) : NotFound(new { error = result.ErrorMessage });
    }

    /// <summary>POST /api/notifications/read-all — đánh dấu mọi thông báo chưa đọc của mình.</summary>
    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var result = await _mediator.Send(new MarkNotificationsReadCommand(GetCurrentUserId()), ct);
        return Ok(new { marked = result.Value });
    }

    /// <summary>POST /api/notifications/send-reminders — quét và gửi thông báo nhắc hạn cho Assignment & ChangeRequest (UC-14).</summary>
    [HttpPost("send-reminders")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendReminders([FromQuery] int leadHours = 24, CancellationToken ct = default)
    {
        var lead = TimeSpan.FromHours(Math.Clamp(leadHours, 1, 24 * 14));
        var created = await _mediator.Send(new SendAssignmentRemindersCommand(lead), ct);
        return Ok(new { created });
    }
}
