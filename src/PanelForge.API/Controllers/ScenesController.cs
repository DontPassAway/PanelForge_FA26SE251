using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Features.Scenes.Commands.CreateScene;
using PanelForge.Application.Features.Scenes.Commands.DeleteScene;
using PanelForge.Application.Features.Scenes.Commands.ReorderScenes;
using PanelForge.Application.Features.Scenes.Commands.UpdateScene;
using PanelForge.Application.Features.Scenes.Models;
using PanelForge.Application.Features.Scenes.Queries.GetSceneById;
using PanelForge.Application.Features.Scenes.Queries.GetScenesByChapter;

namespace PanelForge.API.Controllers;

[ApiController]
public sealed class ScenesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ScenesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/scenes
    /// Tạo một phân cảnh mới phân rã từ Chapter.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/scenes")]
    [ProducesResponseType(typeof(SceneDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateScene(
        [FromRoute] Guid chapterId,
        [FromBody] CreateSceneRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateSceneCommand(
            ChapterId: chapterId,
            UserId: GetCurrentUserId(),
            SceneNumber: body.SceneNumber,
            Heading: body.Heading,
            Summary: body.Summary
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(nameof(GetSceneById), new { sceneId = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// GET /api/chapters/{chapterId}/scenes
    /// Lấy danh sách phân cảnh của một Chapter, sắp xếp theo thứ tự SceneNumber.
    /// </summary>
    [HttpGet("api/chapters/{chapterId:guid}/scenes")]
    [ProducesResponseType(typeof(IReadOnlyList<SceneDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScenesByChapter(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken)
    {
        var query = new GetScenesByChapterQuery(chapterId);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/scenes/{sceneId}
    /// Lấy thông tin chi tiết của một Scene kèm danh sách dòng kịch bản.
    /// </summary>
    [HttpGet("api/scenes/{sceneId:guid}")]
    [ProducesResponseType(typeof(SceneDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSceneById(
        [FromRoute] Guid sceneId,
        CancellationToken cancellationToken)
    {
        var query = new GetSceneByIdQuery(sceneId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/scenes/{sceneId}
    /// Cập nhật nội dung tiêu đề kịch bản và tóm tắt phân cảnh.
    /// </summary>
    [HttpPut("api/scenes/{sceneId:guid}")]
    [ProducesResponseType(typeof(SceneDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateScene(
        [FromRoute] Guid sceneId,
        [FromBody] UpdateSceneRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateSceneCommand(
            SceneId: sceneId,
            UserId: GetCurrentUserId(),
            Heading: body.Heading,
            Summary: body.Summary
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/chapters/{chapterId}/scenes/reorder
    /// Sắp xếp lại thứ tự danh sách phân cảnh trong Chapter.
    /// </summary>
    [HttpPut("api/chapters/{chapterId:guid}/scenes/reorder")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReorderScenes(
        [FromRoute] Guid chapterId,
        [FromBody] ReorderScenesRequest body,
        CancellationToken cancellationToken)
    {
        var command = new ReorderScenesCommand(chapterId, GetCurrentUserId(), body.Scenes);
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.ErrorMessage });

        return Ok(new { message = "Cập nhật thứ tự phân cảnh thành công." });
    }

    /// <summary>
    /// DELETE /api/scenes/{sceneId}
    /// Xóa mềm một phân cảnh (Soft-delete).
    /// </summary>
    [HttpDelete("api/scenes/{sceneId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteScene(
        [FromRoute] Guid sceneId,
        CancellationToken cancellationToken)
    {
        var command = new DeleteSceneCommand(sceneId, GetCurrentUserId());
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(new { message = $"Phân cảnh {sceneId} đã được xóa thành công." });
    }
}
