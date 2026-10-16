using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common.Models;
using PanelForge.Application.Features.Catalog;

namespace PanelForge.API.Controllers;

/// <summary>
/// BR-23 / BR-24: public catalog of Published Series. Không cần đăng nhập và không phụ thuộc workspace membership,
/// là đích của landing "PublicCatalog" (landing-context) và của app Mobile cho Reader.
/// </summary>
[ApiController]
[Route("api/catalog")]
[AllowAnonymous]
public class CatalogController : ControllerBase
{
    private readonly IMediator _mediator;

    public CatalogController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/catalog/series — danh sách Series có ít nhất một chương Published.</summary>
    [HttpGet("series")]
    [ProducesResponseType(typeof(PagedResult<PublicSeriesDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSeries(
        [FromQuery] string? search,
        [FromQuery] string? genre,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPublicCatalogQuery(search, genre, pageIndex, pageSize), cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>GET /api/catalog/series/{seriesId} — thông tin Series và các chương Published (404 nếu chưa có chương nào).</summary>
    [HttpGet("series/{seriesId:guid}")]
    [ProducesResponseType(typeof(PublicSeriesDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeriesDetail([FromRoute] Guid seriesId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPublicSeriesDetailQuery(seriesId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { error = result.ErrorMessage });
    }

    /// <summary>GET /api/catalog/chapters/{chapterId} — nội dung để Reader đọc: hướng đọc và danh sách trang (404 nếu chưa đến giờ phát hành).</summary>
    [HttpGet("chapters/{chapterId:guid}")]
    [ProducesResponseType(typeof(PublicChapterReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReadChapter([FromRoute] Guid chapterId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPublicChapterQuery(chapterId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { error = result.ErrorMessage });
    }

    /// <summary>GET /api/catalog/chapters/{chapterId}/pages/{pageId}/image — ảnh trang (artwork ở stage cuối, bản mới nhất).</summary>
    [HttpGet("chapters/{chapterId:guid}/pages/{pageId:guid}/image")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPageImage([FromRoute] Guid chapterId, [FromRoute] Guid pageId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPublicPageImageQuery(chapterId, pageId), cancellationToken);
        if (!result.IsSuccess) return NotFound(new { error = result.ErrorMessage });

        // Nội dung đã phát hành nên cho cache ngắn; nosniff để trình duyệt không đoán lại loại file
        Response.Headers.CacheControl = "public, max-age=300";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }
}
