using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPipeline;
using PanelForge.Application.Features.SeriesPipeline.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>
/// UC-03 bước 3: Producer cấu hình pipeline stage, vai trò và cổng duyệt của một Series.
/// Mọi thao tác ghi trả về toàn bộ pipeline sau khi thay đổi (transition được dựng lại tự động).
/// </summary>
[ApiController]
[Authorize]
public class SeriesPipelineController : ControllerBase
{
    private readonly IMediator _mediator;

    public SeriesPipelineController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/series/{seriesId}/pipeline — xem stage và transition của pipeline Series.</summary>
    [HttpGet("api/series/{seriesId:guid}/pipeline")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(SeriesPipelineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPipeline([FromRoute] Guid seriesId, CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(new GetSeriesPipelineQuery(seriesId), cancellationToken));

    /// <summary>POST /api/series/{seriesId}/pipeline/stages — thêm stage (chèn tại Position, mặc định cuối).</summary>
    [HttpPost("api/series/{seriesId:guid}/pipeline/stages")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(SeriesPipelineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddStage(
        [FromRoute] Guid seriesId,
        [FromBody] AddPipelineStageRequest body,
        CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(new AddPipelineStageCommand(
            seriesId, body.Name, body.Slug, body.Position, body.ColorCode, body.AllowedRole,
            body.IsApprovalGate, body.EstimatedDurationDays), cancellationToken));

    /// <summary>PUT /api/series/{seriesId}/pipeline/stages/{stageId} — đổi tên, màu, vai trò, cổng duyệt, thời lượng.</summary>
    [HttpPut("api/series/{seriesId:guid}/pipeline/stages/{stageId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(SeriesPipelineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStage(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid stageId,
        [FromBody] UpdatePipelineStageRequest body,
        CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(new UpdatePipelineStageCommand(
            seriesId, stageId, body.Name, body.ColorCode, body.AllowedRole,
            body.IsApprovalGate, body.EstimatedDurationDays), cancellationToken));

    /// <summary>PUT /api/series/{seriesId}/pipeline/stages/order — sắp xếp lại toàn bộ stage.</summary>
    [HttpPut("api/series/{seriesId:guid}/pipeline/stages/order")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(SeriesPipelineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReorderStages(
        [FromRoute] Guid seriesId,
        [FromBody] ReorderPipelineStagesRequest body,
        CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(new ReorderPipelineStagesCommand(seriesId, body.StageIds), cancellationToken));

    /// <summary>DELETE /api/series/{seriesId}/pipeline/stages/{stageId} — xóa stage (409 nếu đang được sử dụng).</summary>
    [HttpDelete("api/series/{seriesId:guid}/pipeline/stages/{stageId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(SeriesPipelineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteStage(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid stageId,
        CancellationToken cancellationToken)
        => ToActionResult(await _mediator.Send(new DeletePipelineStageCommand(seriesId, stageId), cancellationToken));

    private IActionResult ToActionResult(Result<SeriesPipelineDto> result)
    {
        if (result.IsSuccess) return Ok(result.Value);

        return result.ErrorCode switch
        {
            ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
            _ => BadRequest(new { error = result.ErrorMessage })
        };
    }
}
