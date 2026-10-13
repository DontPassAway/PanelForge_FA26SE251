using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.ScriptLines.Commands.CreateScriptLine;
using PanelForge.Application.Features.ScriptLines.Commands.DeleteScriptLine;
using PanelForge.Application.Features.ScriptLines.Commands.ReorderScriptLines;
using PanelForge.Application.Features.ScriptLines.Commands.UpdateScriptLine;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>UC-06: Writer viết script thủ công (không cần AI). Quyền Workspace kiểm qua {chapterId} trên route.</summary>
[ApiController]
[Authorize]
public sealed class ScriptLinesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ScriptLinesController(IMediator mediator) => _mediator = mediator;

    private Guid GetCurrentUserId() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    /// <summary>POST /api/chapters/{chapterId}/scenes/{sceneId}/script-lines — thêm dòng script (thoại hoặc chỉ dẫn).</summary>
    [HttpPost("api/chapters/{chapterId:guid}/scenes/{sceneId:guid}/script-lines")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(ScriptLineDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        Guid chapterId, Guid sceneId, [FromBody] CreateScriptLineRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateScriptLineCommand(
            chapterId, sceneId, GetCurrentUserId(), body.DialogueText, body.StageDirection,
            body.SpeakerCharacterId, body.LineOrder, body.ExpectedChapterVersion, body.BibleEntryIds, body.ClientLineId), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Error(result);
    }

    /// <summary>PUT /api/chapters/{chapterId}/script-lines/{lineId} — sửa dòng (thay toàn bộ nội dung).</summary>
    [HttpPut("api/chapters/{chapterId:guid}/script-lines/{lineId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(ScriptLineDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid chapterId, Guid lineId, [FromBody] UpdateScriptLineRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateScriptLineCommand(
            chapterId, lineId, GetCurrentUserId(), body.DialogueText, body.StageDirection,
            body.SpeakerCharacterId, body.ExpectedChapterVersion, body.BibleEntryIds), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>DELETE /api/chapters/{chapterId}/script-lines/{lineId}?expectedChapterVersion= — xoá mềm dòng.</summary>
    [HttpDelete("api/chapters/{chapterId:guid}/script-lines/{lineId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(DeleteScriptLineResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(
        Guid chapterId, Guid lineId, [FromQuery] long? expectedChapterVersion, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteScriptLineCommand(chapterId, lineId, GetCurrentUserId(), expectedChapterVersion), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>PUT /api/chapters/{chapterId}/scenes/{sceneId}/script-lines/reorder — đổi thứ tự các dòng trong Scene (DnD).</summary>
    [HttpPut("api/chapters/{chapterId:guid}/scenes/{sceneId:guid}/script-lines/reorder")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reorder(
        Guid chapterId, Guid sceneId, [FromBody] ReorderScriptLinesRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new ReorderScriptLinesCommand(
            chapterId, sceneId, GetCurrentUserId(), body.Lines, body.ExpectedChapterVersion), ct);
        return result.IsSuccess ? Ok(new { message = "Cập nhật thứ tự dòng kịch bản thành công." }) : Error(result);
    }

    private IActionResult Error<T>(Result<T> result) => result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
        ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
        ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage, code = result.ErrorCode }),
        _ => BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode })
    };
}
