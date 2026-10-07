using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Models;
using PanelForge.Application.Features.Chapters.Commands.ApproveChapter;
using PanelForge.Application.Features.Chapters.Commands.CreateChapter;
using PanelForge.Application.Features.Chapters.Commands.DeleteChapter;
using PanelForge.Application.Features.Chapters.Commands.PublishChapter;
using PanelForge.Application.Features.Chapters.Commands.ReopenChapter;
using PanelForge.Application.Features.Chapters.Commands.UpdateChapter;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Queries.GetChapterById;
using PanelForge.Application.Features.Chapters.Queries.GetChapterReviews;
using PanelForge.Application.Features.Chapters.Queries.GetChaptersBySeries;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

[ApiController]
[Authorize]
public sealed class ChaptersController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChaptersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// POST /api/series/{seriesId}/chapters
    /// Tạo một tập/chương mới cho Series (UC-03, Release Calendar).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/chapters")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChapterDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateChapter(
        [FromRoute] Guid seriesId,
        [FromBody] CreateChapterRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateChapterCommand(
            SeriesId: seriesId,
            UserId: GetCurrentUserId(),
            ChapterNumber: body.ChapterNumber,
            Title: body.Title,
            TargetReleaseDate: body.TargetReleaseDate
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(nameof(GetChapterById), new { chapterId = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// GET /api/series/{seriesId}/chapters?pageIndex=1&pageSize=20
    /// Lấy danh sách chương của Series có hỗ trợ phân trang (Release Calendar).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/chapters")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(PagedResult<ChapterDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChaptersBySeries(
        [FromRoute] Guid seriesId,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new GetChaptersBySeriesQuery(seriesId, pageIndex, pageSize);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/chapters/{chapterId}
    /// Lấy thông tin chi tiết của một chương kèm danh sách Scene và Page.
    /// </summary>
    [HttpGet("api/chapters/{chapterId:guid}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(ChapterDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChapterById(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken)
    {
        var query = new GetChapterByIdQuery(chapterId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/chapters/{chapterId}
    /// Cập nhật tên, ngày phát hành mục tiêu hoặc duyệt tăng Version của chương (Release Calendar).
    /// </summary>
    [HttpPut("api/chapters/{chapterId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(ChapterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateChapter(
        [FromRoute] Guid chapterId,
        [FromBody] UpdateChapterRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateChapterCommand(
            ChapterId: chapterId,
            UserId: GetCurrentUserId(),
            Title: body.Title,
            TargetReleaseDate: body.TargetReleaseDate,
            IncrementVersion: body.IncrementVersion,
            Outline: body.Outline
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
    /// DELETE /api/chapters/{chapterId}
    /// Xóa mềm một chương (Soft-delete).
    /// </summary>
    [HttpDelete("api/chapters/{chapterId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteChapter(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken)
    {
        var command = new DeleteChapterCommand(chapterId, GetCurrentUserId());
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(new { message = $"Chương {chapterId} đã được xóa thành công." });
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/approve
    /// Producer duyệt bản hoàn chỉnh (InProduction → Approved) và chuyển cho Publisher.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/approve")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChapterReviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveChapter([FromRoute] Guid chapterId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new ApproveChapterCommand(chapterId, GetCurrentUserId()), cancellationToken));

    /// <summary>
    /// POST /api/chapters/{chapterId}/reopen
    /// Producer mở lại, hoặc Publisher từ chối xuất bản: Approved → InProduction, bắt buộc lý do.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/reopen")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Publisher)]
    [ProducesResponseType(typeof(ChapterReviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReopenChapter(
        [FromRoute] Guid chapterId, [FromBody] ReopenChapterRequest body, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new ReopenChapterCommand(chapterId, GetCurrentUserId(), body.Comment), cancellationToken));

    /// <summary>
    /// POST /api/chapters/{chapterId}/publish
    /// Publisher duyệt xuất bản và lên lịch (Approved → Published). publishAt bỏ trống = phát hành ngay.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/publish")]
    [RequireWorkspaceRole(WorkspaceRole.Publisher)]
    [ProducesResponseType(typeof(ChapterReviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishChapter(
        [FromRoute] Guid chapterId, [FromBody] PublishChapterRequest? body, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new PublishChapterCommand(chapterId, GetCurrentUserId(), body?.PublishAt), cancellationToken));

    /// <summary>GET /api/chapters/{chapterId}/reviews — lịch sử phê duyệt cấp Chapter (thành viên Workspace).</summary>
    [HttpGet("api/chapters/{chapterId:guid}/reviews")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<ChapterReviewDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChapterReviews([FromRoute] Guid chapterId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetChapterReviewsQuery(chapterId), cancellationToken));

    private IActionResult ToActionResult<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
        ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
        _ => BadRequest(new { error = result.ErrorMessage })
    };
}
