using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Commands.Editions;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Queries.GetChapterEditions;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

[ApiController]
[Authorize]
public sealed class ChapterEditionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChapterEditionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    private IActionResult ToActionResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : result.ErrorCode switch
        {
            ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage, code = result.ErrorCode }),
            ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
            _ => BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode })
        };

    /// <summary>
    /// GET /api/chapters/{chapterId}/editions
    /// Danh sách ấn bản ngôn ngữ của chương (BR-34).
    /// </summary>
    [HttpGet("api/chapters/{chapterId:guid}/editions")]
    [RequireWorkspaceRole]
    public async Task<IActionResult> GetEditions(Guid chapterId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetChapterEditionsQuery(chapterId), ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/editions/{lang}/submit
    /// Letterer / Artist / Producer nộp bản dịch để Editor thẩm định (Gate 2b).
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/editions/{lang}/submit")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist, WorkspaceRole.Letterer)]
    public async Task<IActionResult> SubmitTranslation(
        Guid chapterId, string lang, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new SubmitEditionTranslationCommand(chapterId, lang, userId), ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/editions/{lang}/review
    /// Editor thẩm định bản dịch (Gate 2b): duyệt hoặc yêu cầu sửa (ChangeRequest).
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/editions/{lang}/review")]
    [RequireWorkspaceRole(WorkspaceRole.Editor)]
    public async Task<IActionResult> ReviewEdition(
        Guid chapterId, string lang, [FromBody] ReviewEditionRequest req, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new ReviewEditionCommand(
            chapterId, lang, userId, req.Action, req.Comment), ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/editions/{lang}/approve
    /// Producer nghiệm thu ấn bản dịch (Gate 3b), tạo Snapshot SHA-256.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/editions/{lang}/approve")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    public async Task<IActionResult> ApproveEdition(
        Guid chapterId, string lang, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new ApproveEditionCommand(chapterId, lang, userId), ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/editions/{lang}/publish
    /// Producer / Publisher phát hành ấn bản dịch đã nghiệm thu (Approved).
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/editions/{lang}/publish")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Publisher)]
    public async Task<IActionResult> PublishEdition(
        Guid chapterId, string lang, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new PublishEditionCommand(chapterId, lang, userId), ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/editions/{lang}/reopen
    /// Producer mở lại ấn bản về Draft kèm lý do.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/editions/{lang}/reopen")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    public async Task<IActionResult> ReopenEdition(
        Guid chapterId, string lang, [FromBody] ReopenEditionRequest req, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new ReopenEditionCommand(chapterId, lang, userId, req.Reason), ct);
        return ToActionResult(result);
    }
}
