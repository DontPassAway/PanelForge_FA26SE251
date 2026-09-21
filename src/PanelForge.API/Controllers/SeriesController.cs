using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Features.Series.Commands.CreateSeries;
using PanelForge.Application.Features.Series.Commands.DeleteSeries;
using PanelForge.Application.Features.Series.Commands.UpdateSeries;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Application.Features.Series.Queries.GetSeriesById;
using PanelForge.Application.Features.Series.Queries.GetSeriesList;

namespace PanelForge.API.Controllers;

[ApiController]
public sealed class SeriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SeriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// POST /api/workspaces/{workspaceId}/series
    /// Tạo dự án truyện tranh mới trong Workspace.
    /// </summary>
    [HttpPost("api/workspaces/{workspaceId:guid}/series")]
    [ProducesResponseType(typeof(SeriesDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSeries(
        [FromRoute] Guid workspaceId,
        [FromBody] CreateSeriesRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateSeriesCommand(
            WorkspaceId: workspaceId,
            UserId: GetCurrentUserId(),
            Title: body.Title,
            Synopsis: body.Synopsis,
            ReadingDirection: body.ReadingDirection,
            PipelineDefinitionId: body.PipelineDefinitionId
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(nameof(GetSeriesById), new { seriesId = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// GET /api/workspaces/{workspaceId}/series
    /// Lấy danh sách Series trong Workspace, hỗ trợ tìm kiếm.
    /// </summary>
    [HttpGet("api/workspaces/{workspaceId:guid}/series")]
    [ProducesResponseType(typeof(IReadOnlyList<SeriesDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSeriesList(
        [FromRoute] Guid workspaceId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = new GetSeriesListQuery(workspaceId, GetCurrentUserId(), search);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/series/{seriesId}
    /// Lấy thông tin chi tiết của một Series kèm danh sách Chapter.
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}")]
    [ProducesResponseType(typeof(SeriesDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeriesById(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var query = new GetSeriesByIdQuery(seriesId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/series/{seriesId}
    /// Cập nhật thông tin Series.
    /// </summary>
    [HttpPut("api/series/{seriesId:guid}")]
    [ProducesResponseType(typeof(SeriesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSeries(
        [FromRoute] Guid seriesId,
        [FromBody] UpdateSeriesRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateSeriesCommand(
            SeriesId: seriesId,
            UserId: GetCurrentUserId(),
            Title: body.Title,
            Synopsis: body.Synopsis,
            ReadingDirection: body.ReadingDirection,
            PipelineDefinitionId: body.PipelineDefinitionId
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
    /// DELETE /api/series/{seriesId}
    /// Xóa mềm một Series (Soft-delete).
    /// </summary>
    [HttpDelete("api/series/{seriesId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSeries(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var command = new DeleteSeriesCommand(seriesId, GetCurrentUserId());
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(new { message = $"Series {seriesId} đã được xóa thành công." });
    }
}
