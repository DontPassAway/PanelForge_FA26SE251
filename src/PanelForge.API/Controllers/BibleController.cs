using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.CreateBibleEntry;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntriesAtChapter;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntryHistory;
using PanelForge.Application.Features.Bible.Queries.GetSeriesBible;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

[ApiController]
[Authorize]
public sealed class BibleController : ControllerBase
{
    private readonly IMediator _mediator;

    public BibleController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// GET /api/series/{seriesId}/bible
    /// Lấy toàn bộ danh mục Series Bible kèm revision mới nhất của từng mục (UC-04).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/bible")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(SeriesBibleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeriesBible(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var query = new GetSeriesBibleQuery(seriesId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/series/{seriesId}/bible/at-chapter/{chapterNumber}
    /// Lấy snapshot Series Bible có hiệu lực tại một Chapter cụ thể (BR-15).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/bible/at-chapter/{chapterNumber:int}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<BibleEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBibleEntriesAtChapter(
        [FromRoute] Guid seriesId,
        [FromRoute] int chapterNumber,
        CancellationToken cancellationToken)
    {
        var query = new GetBibleEntriesAtChapterQuery(seriesId, chapterNumber);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries
    /// Thêm mục mới vào Series Bible (Tự động khởi tạo Version 1 - BR-18, CF1 Step 8).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(BibleEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateBibleEntry(
        [FromRoute] Guid seriesId,
        [FromBody] CreateBibleEntryRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateBibleEntryCommand(
            SeriesId: seriesId,
            UserId: GetCurrentUserId(),
            Category: body.Category,
            Code: body.Code,
            Name: body.Name,
            Description: body.Description,
            Subtitle: body.Subtitle,
            DetailsJson: body.DetailsJson,
            ReferenceImageUrl: body.ReferenceImageUrl,
            Priority: body.Priority,
            StrictCheck: body.StrictCheck,
            EffectiveFromChapterNumber: body.EffectiveFromChapterNumber
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(nameof(GetSeriesBible), new { seriesId }, result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions
    /// Tạo phiên bản sửa đổi mới cho BibleEntry có hiệu lực từ một Chapter (Append-only BR-01, BR-15).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(BibleEntryRevisionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddBibleEntryRevision(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromBody] AddBibleEntryRevisionRequest body,
        CancellationToken cancellationToken)
    {
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: seriesId,
            BibleEntryId: entryId,
            UserId: GetCurrentUserId(),
            Summary: body.Summary,
            Name: body.Name,
            Description: body.Description,
            Subtitle: body.Subtitle,
            DetailsJson: body.DetailsJson,
            ReferenceImageUrl: body.ReferenceImageUrl,
            Priority: body.Priority,
            StrictCheck: body.StrictCheck,
            EffectiveFromChapterNumber: body.EffectiveFromChapterNumber,
            ExpectedVersionNumber: body.ExpectedVersionNumber
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/series/{seriesId}/bible/entries/{entryId}/history
    /// Lấy toàn bộ lịch sử các phiên bản của một mục trong Bible (BR-18).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/history")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<BibleEntryRevisionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBibleEntryHistory(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        CancellationToken cancellationToken)
    {
        var query = new GetBibleEntryHistoryQuery(seriesId, entryId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }
}
