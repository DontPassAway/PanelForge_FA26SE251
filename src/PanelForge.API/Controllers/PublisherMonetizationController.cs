using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Publisher.Monetization;

namespace PanelForge.API.Controllers;

/// <summary>
/// Publisher quản lý kinh doanh theo bộ truyện: dashboard (lượt xem, lượt mua, đánh giá, doanh thu — lọc theo ngôn ngữ),
/// và Manage từng chương (giá theo ngôn ngữ, ảnh trang bản dịch, N trang đầu xem trước theo từng ấn bản — BR-33).
/// Chỉ Publisher/Admin; Producer không còn khóa trang hay xem doanh thu.
/// </summary>
[ApiController]
[Authorize]
[Route("api/publisher")]
public sealed class PublisherMonetizationController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublisherMonetizationController(IMediator mediator) => _mediator = mediator;

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    /// <summary>GET /api/publisher/series/{seriesId}/insights?language=vi|en|ja|all — dashboard BR-32 (Publisher &amp; Admin chỉ đọc).</summary>
    [HttpGet("series/{seriesId:guid}/insights")]
    [Authorize(Roles = "Publisher,Admin")]
    [ProducesResponseType(typeof(SeriesInsightsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInsights([FromRoute] Guid seriesId, [FromQuery] string? language, CancellationToken ct)
        => ToResult(await _mediator.Send(new GetSeriesInsightsQuery(seriesId, language), ct));

    /// <summary>GET /api/publisher/chapters/{chapterId}/manage — trang, ảnh dịch, quy định thương mại từng ấn bản (chỉ Publisher).</summary>
    [HttpGet("chapters/{chapterId:guid}/manage")]
    [Authorize(Roles = "Publisher")]
    [ProducesResponseType(typeof(ChapterManageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetManage([FromRoute] Guid chapterId, CancellationToken ct)
        => ToResult(await _mediator.Send(new GetChapterManageQuery(chapterId), ct));

    /// <summary>
    /// PUT /api/publisher/chapters/{chapterId}/editions/{lang}/monetization — BR-33 / D4 (<c>SetEditionMonetization</c>):
    /// loại Free/Trả phí, giá VNĐ và N trang đầu xem trước của ấn bản. Chỉ tài khoản Publisher (BR-33.3: 403 với mọi
    /// vai trò khác, kể cả Admin/Producer). Đổi loại hoặc giá ấn bản đã phát hành cần lý do; mọi thay đổi ghi Audit.
    /// </summary>
    [HttpPut("chapters/{chapterId:guid}/editions/{lang}/monetization")]
    [Authorize(Roles = "Publisher")]
    [ProducesResponseType(typeof(ChapterManageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetEditionMonetization(
        [FromRoute] Guid chapterId, [FromRoute] string lang,
        [FromBody] SetEditionMonetizationRequest request, CancellationToken ct)
        => ToResult(await _mediator.Send(new SetEditionMonetizationCommand(
            chapterId, lang, request.IsPaid, request.PriceVnd, request.FreePreviewPages, request.Reason, CurrentUserId), ct));

    /// <summary>GET /api/publisher/chapters/{chapterId}/pages/{pageId}/image?lang= — ảnh xem trước cho Publisher.</summary>
    [HttpGet("chapters/{chapterId:guid}/pages/{pageId:guid}/image")]
    [Authorize(Roles = "Publisher")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPageImage(
        [FromRoute] Guid chapterId, [FromRoute] Guid pageId, [FromQuery] string? lang, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPublisherPageImageQuery(chapterId, pageId, lang), ct);
        if (!result.IsSuccess) return NotFound(new { error = result.ErrorMessage });

        Response.Headers.CacheControl = "private, max-age=60";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    private IActionResult ToResult<T>(Result<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        return result.ErrorCode switch
        {
            ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode })
        };
    }
}
