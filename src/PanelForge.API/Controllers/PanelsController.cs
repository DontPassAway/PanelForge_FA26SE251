using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common;
using PanelForge.API.Common.Attributes;
using PanelForge.Domain.Enums;
using PanelForge.Application.Features.Panels.Commands.AssignPanelScene;
using PanelForge.Application.Features.Panels.Commands.AssignPanelScriptLines;
using PanelForge.Application.Features.Panels.Commands.AutoAssignScriptLines;
using PanelForge.Application.Features.Panels.Commands.CreatePanel;
using PanelForge.Application.Features.Panels.Commands.DeletePanel;
using PanelForge.Application.Features.Panels.Commands.UpdatePanelCoordinates;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Application.Features.Panels.Queries.GetPanelById;
using PanelForge.Application.Features.Panels.Queries.GetPanelsByPage;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Consistency.Models;

namespace PanelForge.API.Controllers;

[ApiController]
public sealed class PanelsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PanelsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// POST /api/pages/{pageId}/panels
    /// Tạo một Panel (khung tranh) trên trang với tọa độ Polygon/BoundingBox.
    /// </summary>
    [HttpPost("api/pages/{pageId:guid}/panels")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(PanelDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePanel(
        [FromRoute] Guid pageId,
        [FromBody] CreatePanelRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreatePanelCommand(
            PageId: pageId,
            UserId: GetCurrentUserId(),
            PanelNumber: body.PanelNumber,
            ReadingOrder: body.ReadingOrder,
            BoundingBox: body.BoundingBox
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return this.ConflictOrNull(result) ?? BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(nameof(GetPanelById), new { panelId = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// GET /api/pages/{pageId}/panels
    /// Lấy danh sách các Panel trên trang theo thứ tự đọc (ReadingOrder).
    /// </summary>
    [HttpGet("api/pages/{pageId:guid}/panels")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<PanelDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPanelsByPage(
        [FromRoute] Guid pageId,
        CancellationToken cancellationToken)
    {
        var query = new GetPanelsByPageQuery(pageId);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/panels/{panelId}
    /// Lấy thông tin chi tiết của một Panel kèm các phần tử Elements bên trong.
    /// </summary>
    [HttpGet("api/panels/{panelId:guid}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(PanelDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPanelById(
        [FromRoute] Guid panelId,
        CancellationToken cancellationToken)
    {
        var query = new GetPanelByIdQuery(panelId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/panels/{panelId}/coordinates
    /// Cập nhật kéo thả tọa độ Polygon/BoundingBox và thứ tự đọc của Panel.
    /// </summary>
    [HttpPut("api/panels/{panelId:guid}/coordinates")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(PanelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePanelCoordinates(
        [FromRoute] Guid panelId,
        [FromBody] UpdatePanelCoordinatesRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpdatePanelCoordinatesCommand(
            PanelId: panelId,
            UserId: GetCurrentUserId(),
            BoundingBox: body.BoundingBox,
            ReadingOrder: body.ReadingOrder,
            ExpectedVersion: body.ExpectedVersion
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return this.ConflictOrNull(result) ?? BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(result.Value);
    }

    /// <summary>PUT /api/panels/{panelId}/scene — gán Panel vào Scene (V1.2); sceneId = null để bỏ gán.</summary>
    [HttpPut("api/panels/{panelId:guid}/scene")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(PanelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignPanelScene(
        [FromRoute] Guid panelId,
        [FromBody] AssignPanelSceneRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AssignPanelSceneCommand(panelId, body.SceneId, GetCurrentUserId()), cancellationToken);
        if (!result.IsSuccess)
            return result.ErrorMessage?.Contains("không tồn tại") == true
                ? NotFound(new { error = result.ErrorMessage })
                : this.ConflictOrNull(result) ?? BadRequest(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/pages/{pageId}/script-lines/auto-assign — gom dòng của Scene thành cụm (chỉ dẫn + thoại theo sau)
    /// và gán lần lượt vào Panel của Page theo thứ tự đọc; dryRun = chỉ xem gợi ý.
    /// </summary>
    [HttpPost("api/pages/{pageId:guid}/script-lines/auto-assign")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(AutoAssignScriptLinesResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AutoAssignScriptLines(
        [FromRoute] Guid pageId,
        [FromBody] AutoAssignScriptLinesRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AutoAssignScriptLinesCommand(pageId, body.SceneId, body.DryRun, GetCurrentUserId()), cancellationToken);
        if (!result.IsSuccess)
            return result.ErrorMessage?.Contains("không tồn tại") == true
                ? NotFound(new { error = result.ErrorMessage })
                : this.ConflictOrNull(result) ?? BadRequest(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>PUT /api/panels/{panelId}/script-lines — đặt tập ScriptLine neo vào Panel (thay thế tập cũ).</summary>
    [HttpPut("api/panels/{panelId:guid}/script-lines")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(IReadOnlyList<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignPanelScriptLines(
        [FromRoute] Guid panelId,
        [FromBody] AssignPanelScriptLinesRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AssignPanelScriptLinesCommand(panelId, body.ScriptLineIds ?? [], GetCurrentUserId()), cancellationToken);
        if (!result.IsSuccess)
            return result.ErrorMessage?.Contains("không tồn tại") == true
                ? NotFound(new { error = result.ErrorMessage })
                : this.ConflictOrNull(result) ?? BadRequest(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// DELETE /api/panels/{panelId}
    /// Xóa mềm một Panel khỏi trang (Soft-delete).
    /// </summary>
    [HttpDelete("api/panels/{panelId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePanel(
        [FromRoute] Guid panelId,
        CancellationToken cancellationToken)
    {
        var command = new DeletePanelCommand(panelId, GetCurrentUserId());
        var result = await _mediator.Send(command, cancellationToken);

        // 409: panel còn việc đang giao
        if (!result.IsSuccess)
            return this.ConflictOrNull(result) ?? NotFound(new { error = result.ErrorMessage });

        return Ok(new { message = $"Panel {panelId} đã được xóa thành công." });
    }

    /// <summary>
    /// POST /api/panels/{panelId}/check-image-consistency
    /// Chạy kiểm tra nhất quán hình ảnh AI (Bước A5) giữa Artwork của Panel và Character Sheet đã khóa.
    /// </summary>
    [HttpPost("api/panels/{panelId:guid}/check-image-consistency")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(ImageConsistencyResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckImageConsistency(
        [FromRoute] Guid panelId,
        [FromQuery] string? revisionRef,
        [FromQuery] decimal? threshold,
        [FromServices] IImageConsistencyService imageConsistencyService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await imageConsistencyService.CheckPanelConsistencyAsync(
                panelId, revisionRef, threshold, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
