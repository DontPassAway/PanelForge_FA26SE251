using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Features.ConsistencyRules.Commands;
using PanelForge.Application.Features.ConsistencyRules.Queries;
using PanelForge.Application.Features.Series.Commands.CreateSeries;
using PanelForge.Application.Features.Series.Commands.DeleteSeries;
using PanelForge.Application.Features.Series.Commands.UpdateSeries;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Application.Features.Series.Queries.GetSeriesById;
using PanelForge.Application.Features.Series.Queries.GetSeriesList;
using PanelForge.Application.Features.TypographyPresets.Commands;
using PanelForge.Application.Features.TypographyPresets.Queries;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

[ApiController]
[Authorize]
public sealed class SeriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SeriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// POST /api/workspaces/{workspaceId}/series
    /// Tạo dự án truyện tranh mới trong Workspace (UC-03). 
    /// Task 7: Clones stages from PipelineTemplate (DB-backed).
    /// </summary>
    [HttpPost("api/workspaces/{workspaceId:guid}/series")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
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
            PipelineTemplateId: body.PipelineTemplateId,
            Genre: body.Genre,
            Format: body.Format,
            ReleaseScheduleJson: body.ReleaseScheduleJson
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
    /// </summary>
    [HttpGet("api/workspaces/{workspaceId:guid}/series")]
    [RequireWorkspaceRole]
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
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}")]
    [RequireWorkspaceRole]
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
    /// </summary>
    [HttpPut("api/series/{seriesId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
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
            PipelineDefinitionId: body.PipelineDefinitionId,
            Genre: body.Genre,
            Format: body.Format,
            ReleaseScheduleJson: body.ReleaseScheduleJson
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
    /// </summary>
    [HttpDelete("api/series/{seriesId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
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

    // ─── Task 8: Typography Presets (replaces old SeriesPreset JSON endpoint) ──

    /// <summary>
    /// GET /api/series/{seriesId}/presets
    /// Lấy danh sách TypographyPresets của Series (Task 8, NFR-08).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/presets")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<TypographyPresetDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTypographyPresets(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTypographyPresetsQuery(seriesId), cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/presets
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/presets")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(TypographyPresetDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTypographyPreset(
        [FromRoute] Guid seriesId,
        [FromBody] CreateTypographyPresetRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateTypographyPresetCommand(
            SeriesId: seriesId,
            RequestingUserId: GetCurrentUserId(),
            Name: body.Name,
            FontFamily: body.FontFamily,
            FontSize: body.FontSize,
            FontWeight: body.FontWeight,
            FontStyle: body.FontStyle,
            LineHeight: body.LineHeight,
            LetterSpacing: body.LetterSpacing,
            TextAlign: body.TextAlign,
            UsageType: body.UsageType,
            IsDefault: body.IsDefault
        );
        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// PUT /api/series/{seriesId}/presets/{presetId}
    /// </summary>
    [HttpPut("api/series/{seriesId:guid}/presets/{presetId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(TypographyPresetDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTypographyPreset(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid presetId,
        [FromBody] UpdateTypographyPresetRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateTypographyPresetCommand(
            PresetId: presetId,
            SeriesId: seriesId,
            RequestingUserId: GetCurrentUserId(),
            Name: body.Name,
            FontFamily: body.FontFamily,
            FontSize: body.FontSize,
            FontWeight: body.FontWeight,
            FontStyle: body.FontStyle,
            LineHeight: body.LineHeight,
            LetterSpacing: body.LetterSpacing,
            TextAlign: body.TextAlign,
            UsageType: body.UsageType,
            IsDefault: body.IsDefault
        );
        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return Ok(result.Value);
    }

    /// <summary>
    /// DELETE /api/series/{seriesId}/presets/{presetId}
    /// </summary>
    [HttpDelete("api/series/{seriesId:guid}/presets/{presetId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    public async Task<IActionResult> DeleteTypographyPreset(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid presetId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new DeleteTypographyPresetCommand(presetId, seriesId, GetCurrentUserId()), cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return Ok(new { message = result.Value });
    }

    // ─── Task 9: Consistency Rules (replaces old SeriesPreset JSON endpoint) ───

    /// <summary>
    /// GET /api/series/{seriesId}/consistency-rules
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/consistency-rules")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<ConsistencyRuleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConsistencyRules(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetConsistencyRulesQuery(seriesId), cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/consistency-rules
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/consistency-rules")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ConsistencyRuleDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateConsistencyRule(
        [FromRoute] Guid seriesId,
        [FromBody] CreateConsistencyRuleRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateConsistencyRuleCommand(
            SeriesId: seriesId,
            RequestingUserId: GetCurrentUserId(),
            Name: body.Name,
            RuleType: body.RuleType,
            Description: body.Description,
            IsEnabled: body.IsEnabled,
            Pattern: body.Pattern,
            Severity: body.Severity,
            ConfigurationJson: body.ConfigurationJson
        );
        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// PUT /api/series/{seriesId}/consistency-rules/{ruleId}
    /// </summary>
    [HttpPut("api/series/{seriesId:guid}/consistency-rules/{ruleId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ConsistencyRuleDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConsistencyRule(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid ruleId,
        [FromBody] UpdateConsistencyRuleRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateConsistencyRuleCommand(
            RuleId: ruleId,
            SeriesId: seriesId,
            RequestingUserId: GetCurrentUserId(),
            Name: body.Name,
            RuleType: body.RuleType,
            Description: body.Description,
            IsEnabled: body.IsEnabled,
            Pattern: body.Pattern,
            Severity: body.Severity,
            ConfigurationJson: body.ConfigurationJson
        );
        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return Ok(result.Value);
    }

    /// <summary>
    /// DELETE /api/series/{seriesId}/consistency-rules/{ruleId}
    /// </summary>
    [HttpDelete("api/series/{seriesId:guid}/consistency-rules/{ruleId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    public async Task<IActionResult> DeleteConsistencyRule(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid ruleId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new DeleteConsistencyRuleCommand(ruleId, seriesId, GetCurrentUserId()), cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { error = result.ErrorMessage });
        return Ok(new { message = result.Value });
    }
}
