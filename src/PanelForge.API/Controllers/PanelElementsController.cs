using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Features.ContentElements.Commands.CreateElement;
using PanelForge.Application.Features.ContentElements.Commands.DeleteElement;
using PanelForge.Application.Features.ContentElements.Commands.UpdateElement;
using PanelForge.Application.Features.ContentElements.Models;
using PanelForge.Application.Features.ContentElements.Queries.GetElementById;
using PanelForge.Application.Features.ContentElements.Queries.GetElementsByPanel;

namespace PanelForge.API.Controllers;

[ApiController]
public sealed class PanelElementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PanelElementsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// POST /api/panels/{panelId}/elements
    /// Thêm một Element (bóng thoại, ảnh, thoại, SFX) vào trong Panel.
    /// </summary>
    [HttpPost("api/panels/{panelId:guid}/elements")]
    [ProducesResponseType(typeof(ContentElementDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateElement(
        [FromRoute] Guid panelId,
        [FromBody] CreateContentElementRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateContentElementCommand(
            PanelId: panelId,
            UserId: GetCurrentUserId(),
            ElementType: body.ElementType,
            ZIndex: body.ZIndex,
            Content: body.Content,
            TransformGeometry: body.TransformGeometry,
            StyleProperties: body.StyleProperties,
            ScriptLineId: body.ScriptLineId,
            SpeakerCharacterId: body.SpeakerCharacterId
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(nameof(GetElementById), new { elementId = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// GET /api/panels/{panelId}/elements
    /// Lấy danh sách Elements trong Panel được sắp xếp theo Z-Index.
    /// </summary>
    [HttpGet("api/panels/{panelId:guid}/elements")]
    [ProducesResponseType(typeof(IReadOnlyList<ContentElementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetElementsByPanel(
        [FromRoute] Guid panelId,
        CancellationToken cancellationToken)
    {
        var query = new GetContentElementsByPanelQuery(panelId);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/elements/{elementId}
    /// Lấy thông tin chi tiết một Element.
    /// </summary>
    [HttpGet("api/elements/{elementId:guid}")]
    [ProducesResponseType(typeof(ContentElementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetElementById(
        [FromRoute] Guid elementId,
        CancellationToken cancellationToken)
    {
        var query = new GetContentElementByIdQuery(elementId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/elements/{elementId}
    /// Cập nhật vị trí transform, nội dung / link asset URL, style hoặc Z-Index của Element.
    /// </summary>
    [HttpPut("api/elements/{elementId:guid}")]
    [ProducesResponseType(typeof(ContentElementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateElement(
        [FromRoute] Guid elementId,
        [FromBody] UpdateContentElementRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateContentElementCommand(
            ElementId: elementId,
            UserId: GetCurrentUserId(),
            Content: body.Content,
            TransformGeometry: body.TransformGeometry,
            StyleProperties: body.StyleProperties,
            ZIndex: body.ZIndex,
            ScriptLineId: body.ScriptLineId,
            SpeakerCharacterId: body.SpeakerCharacterId
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
    /// DELETE /api/elements/{elementId}
    /// Xóa mềm một Element khỏi Panel (Soft-delete).
    /// </summary>
    [HttpDelete("api/elements/{elementId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteElement(
        [FromRoute] Guid elementId,
        CancellationToken cancellationToken)
    {
        var command = new DeleteContentElementCommand(elementId, GetCurrentUserId());
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(new { message = $"Element {elementId} đã được xóa thành công." });
    }
}
