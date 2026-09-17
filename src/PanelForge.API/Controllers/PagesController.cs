using MediatR;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.DTOs.Content;
using PanelForge.Application.Features.Pages.CreatePage;
using PanelForge.Application.Features.Pages.DeletePage;
using PanelForge.Application.Features.Pages.GetPageById;
using PanelForge.Application.Features.Pages.GetPagesByChapter;
using PanelForge.Application.Features.Pages.UpdateCanvasSettings;

namespace PanelForge.API.Controllers;

[ApiController]
public sealed class PagesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PagesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/pages
    /// Tạo một trang truyện mới trong chương.
    /// Khởi tạo Stream mới trên Marten Event Store và lưu Read Model vào EF Core.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/pages")]
    [ProducesResponseType(typeof(PageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePage(
        [FromRoute] Guid chapterId,
        [FromBody] CreatePageRequestBody body,
        CancellationToken cancellationToken)
    {
        var command = new CreatePageCommand(
            ChapterId:    chapterId,
            PageNumber:   body.PageNumber,
            LayoutFormat: body.LayoutFormat ?? "StandardPage",
            WidthPx:      body.WidthPx > 0 ? body.WidthPx : 1200,
            HeightPx:     body.HeightPx > 0 ? body.HeightPx : 1800,
            Dpi:          body.Dpi > 0 ? body.Dpi : 300,
            SceneId:      body.SceneId
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(
            nameof(GetPageById),
            new { pageId = result.Value!.Id },
            result.Value
        );
    }

    /// <summary>
    /// GET /api/chapters/{chapterId}/pages
    /// Lấy danh sách tất cả các trang của một chương, sắp xếp theo thứ tự trang.
    /// </summary>
    [HttpGet("api/chapters/{chapterId:guid}/pages")]
    [ProducesResponseType(typeof(IReadOnlyList<PageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPagesByChapter(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken)
    {
        var query = new GetPagesByChapterQuery(chapterId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/pages/{pageId}
    /// Lấy thông tin chi tiết của một trang (rebuilt từ Marten Event Store).
    /// </summary>
    [HttpGet("api/pages/{pageId:guid}")]
    [ProducesResponseType(typeof(PageDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPageById(
        [FromRoute] Guid pageId,
        CancellationToken cancellationToken)
    {
        var query = new GetPageByIdQuery(pageId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/pages/{pageId}/canvas-settings
    /// Cập nhật thông số khổ giấy / canvas của trang.
    /// </summary>
    [HttpPut("api/pages/{pageId:guid}/canvas-settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCanvasSettings(
        [FromRoute] Guid pageId,
        [FromBody] UpdateCanvasRequestBody body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateCanvasSettingsCommand(
            PageId:       pageId,
            WidthPx:      body.WidthPx,
            HeightPx:     body.HeightPx,
            Dpi:          body.Dpi,
            LayoutFormat: body.LayoutFormat
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new { message = "Cập nhật thông số canvas thành công." });
    }

    /// <summary>
    /// DELETE /api/pages/{pageId}
    /// Xóa một trang truyện khỏi chương.
    /// </summary>
    [HttpDelete("api/pages/{pageId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePage(
        [FromRoute] Guid pageId,
        CancellationToken cancellationToken)
    {
        var command = new DeletePageCommand(pageId);
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(new { message = $"Trang {pageId} đã được xóa thành công." });
    }
}

// ── Request bodies ───────────────────────────────────────────────────────────

public sealed record CreatePageRequestBody(
    int? PageNumber = null,
    string? LayoutFormat = "StandardPage",
    int WidthPx = 1200,
    int HeightPx = 1800,
    int Dpi = 300,
    Guid? SceneId = null
);

public sealed record UpdateCanvasRequestBody(
    int WidthPx,
    int HeightPx,
    int Dpi,
    string LayoutFormat
);
