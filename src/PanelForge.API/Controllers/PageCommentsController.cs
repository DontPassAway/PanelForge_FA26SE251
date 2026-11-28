using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.PageComments.Commands.ConvertPageCommentToRework;
using PanelForge.Application.Features.PageComments.Commands.CreatePageComment;
using PanelForge.Application.Features.PageComments.Commands.ReopenPageComment;
using PanelForge.Application.Features.PageComments.Commands.ResolvePageComment;
using PanelForge.Application.Features.PageComments.Models;
using PanelForge.Application.Features.PageComments.Queries.GetSeriesPageComments;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>
/// Góp ý nội bộ ghim theo trang/khung tranh trong màn Đọc bộ truyện (Producer, Editor).
/// </summary>
[ApiController]
[Authorize]
public sealed class PageCommentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PageCommentsController(IMediator mediator) => _mediator = mediator;

    private Guid GetCurrentUserId() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    /// <summary>GET /api/series/{seriesId}/page-comments?chapterId=&amp;status=</summary>
    [HttpGet("api/series/{seriesId:guid}/page-comments")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(IReadOnlyList<PageCommentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid seriesId,
        [FromQuery] Guid? chapterId,
        [FromQuery] PageCommentStatus? status,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSeriesPageCommentsQuery(seriesId, chapterId, status), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST /api/chapters/{chapterId}/page-comments</summary>
    [HttpPost("api/chapters/{chapterId:guid}/page-comments")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(PageCommentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        Guid chapterId,
        [FromBody] CreatePageCommentRequest body,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreatePageCommentCommand(
                chapterId,
                GetCurrentUserId(),
                body.PageId,
                body.PanelId,
                body.AssetReferenceId,
                body.PinX,
                body.PinY,
                body.Message),
            ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Error(result);
    }

    /// <summary>POST /api/chapters/{chapterId}/page-comments/{commentId}/resolve</summary>
    [HttpPost("api/chapters/{chapterId:guid}/page-comments/{commentId:guid}/resolve")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(PageCommentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resolve(
        Guid chapterId,
        Guid commentId,
        [FromBody] ResolvePageCommentRequest? body,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ResolvePageCommentCommand(chapterId, commentId, GetCurrentUserId(), body?.Note),
            ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST /api/chapters/{chapterId}/page-comments/{commentId}/reopen</summary>
    [HttpPost("api/chapters/{chapterId:guid}/page-comments/{commentId:guid}/reopen")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(PageCommentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen(
        Guid chapterId,
        Guid commentId,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ReopenPageCommentCommand(chapterId, commentId, GetCurrentUserId()),
            ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/page-comments/{commentId}/rework — chuyển góp ý thành yêu cầu sửa; ô đã duyệt được mở lại
    /// (REV-09, Q27: Editor chính, Producer dự phòng; sau Gate 3 → 409).
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/page-comments/{commentId:guid}/rework")]
    [RequireWorkspaceRole(WorkspaceRole.Editor, WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(PageCommentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConvertToRework(
        Guid chapterId,
        Guid commentId,
        [FromBody] ConvertPageCommentToReworkRequest body,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ConvertPageCommentToReworkCommand(
                chapterId,
                commentId,
                GetCurrentUserId(),
                body.StageTaskId,
                body.Reason),
            ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    private IActionResult Error<T>(Result<T> result) => result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
        ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
        ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage, code = result.ErrorCode }),
        _ => BadRequest(new { error = result.ErrorMessage })
    };
}
