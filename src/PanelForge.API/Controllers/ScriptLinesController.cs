using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.ScriptLines.Commands.CreateScriptLine;
using PanelForge.Application.Features.ScriptLines.Commands.BatchSaveScriptLines;
using PanelForge.Application.Features.ScriptLines.Commands.DeleteScriptLine;
using PanelForge.Application.Features.ScriptLines.Commands.ReorderScriptLines;
using PanelForge.Application.Features.ScriptLines.Commands.RestoreScriptRevision;
using PanelForge.Application.Features.ScriptLines.Commands.LinkBibleEntries;
using PanelForge.Application.Features.ScriptLines.Commands.UpdateScriptLine;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Application.Features.ScriptLines.Queries.GetScriptRevision;
using PanelForge.Domain.Enums;
using PanelForge.API.Common;

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
            body.SpeakerCharacterId, body.LineOrder, body.ExpectedChapterVersion, body.BibleEntryIds, body.ClientLineId, body.NarrationMode), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Error(result);
    }

    /// <summary>POST /api/chapters/{chapterId}/scenes/{sceneId}/script-lines/batch — Lưu đồng bộ hàng loạt các dòng kịch bản trong 1 giao dịch duy nhất.</summary>
    [HttpPost("api/chapters/{chapterId:guid}/scenes/{sceneId:guid}/script-lines/batch")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(BatchSaveScriptLinesResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchSave(
        Guid chapterId, Guid sceneId, [FromBody] BatchSaveScriptLinesRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new BatchSaveScriptLinesCommand(
            chapterId, sceneId, GetCurrentUserId(), body.Lines, body.ExpectedChapterVersion, body.Summary), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
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
            body.SpeakerCharacterId, body.ExpectedChapterVersion, body.BibleEntryIds, body.NarrationMode), ct);
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

    /// <summary>POST /api/chapters/{chapterId}/script-lines/{lineId}/bible-links — gắn liên kết @-mentions từ kịch bản vào Series Bible (hỗ trợ Stub Entity).</summary>
    [HttpPost("api/chapters/{chapterId:guid}/script-lines/{lineId:guid}/bible-links")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(IReadOnlyList<ScriptLineBibleLinkDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LinkBibleEntries(
        Guid chapterId, Guid lineId, [FromBody] LinkBibleEntriesRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new LinkBibleEntriesCommand(
            GetCurrentUserId(), chapterId, lineId, body.Mentions), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>PUT /api/chapters/{chapterId}/scenes/{sceneId}/script-lines/reorder — đổi thứ tự các dòng trong Scene (DnD).</summary>
    [HttpPut("api/chapters/{chapterId:guid}/scenes/{sceneId:guid}/script-lines/reorder")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reorder(
        Guid chapterId, Guid sceneId, [FromBody] ReorderScriptLinesRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new ReorderScriptLinesCommand(
            chapterId, sceneId, GetCurrentUserId(), body.Lines, body.ExpectedChapterVersion), ct);
        return result.IsSuccess ? Ok(new { message = "Cập nhật thứ tự dòng kịch bản thành công." }) : Error(result);
    }

    /// <summary>GET /api/chapters/{chapterId}/scenes/{sceneId}/script-revisions/{revisionId} — các dòng của một revision script (UC-18).</summary>
    [HttpGet("api/chapters/{chapterId:guid}/scenes/{sceneId:guid}/script-revisions/{revisionId:guid}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(ScriptRevisionDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRevision(Guid chapterId, Guid sceneId, Guid revisionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetScriptRevisionQuery(chapterId, sceneId, revisionId), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/scenes/{sceneId}/script-revisions/{revisionId}/restore — khôi phục script của Scene về
    /// revision cũ (UC-13). Tạo revision mới ở đầu lịch sử, không xoá bản nào (BR-08).
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/scenes/{sceneId:guid}/script-revisions/{revisionId:guid}/restore")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(RestoreScriptRevisionResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> RestoreRevision(
        Guid chapterId, Guid sceneId, Guid revisionId, [FromBody] RestoreScriptRevisionRequest? body, CancellationToken ct)
    {
        var result = await _mediator.Send(new RestoreScriptRevisionCommand(
            chapterId, sceneId, revisionId, GetCurrentUserId(), body?.ExpectedChapterVersion), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    private IActionResult Error<T>(Result<T> result) => result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
        ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
        ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage, code = result.ErrorCode }),
        _ => BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode })
    };
}
