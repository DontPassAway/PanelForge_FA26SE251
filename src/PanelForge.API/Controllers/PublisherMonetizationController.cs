using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Publisher.Monetization;

namespace PanelForge.API.Controllers;

/// <summary>
/// Publisher quản lý kinh doanh theo bộ truyện: dashboard (lượt xem, lượt mua, đánh giá, doanh thu — lọc theo ngôn ngữ),
/// và Manage từng chương (giá theo ngôn ngữ, ảnh trang bản dịch, khóa trang trả phí, mặc định khóa N trang cuối).
/// Chỉ Publisher/Admin; Producer không còn khóa trang hay xem doanh thu.
/// </summary>
[ApiController]
[Authorize(Roles = "Publisher,Admin")]
[Route("api/publisher")]
public sealed class PublisherMonetizationController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublisherMonetizationController(IMediator mediator) => _mediator = mediator;

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    /// <summary>GET /api/publisher/series/{seriesId}/insights?language=vi|en|ja|all — dashboard series + số liệu từng chương.</summary>
    [HttpGet("series/{seriesId:guid}/insights")]
    [ProducesResponseType(typeof(SeriesInsightsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInsights([FromRoute] Guid seriesId, [FromQuery] string? language, CancellationToken ct)
        => ToResult(await _mediator.Send(new GetSeriesInsightsQuery(seriesId, language), ct));

    /// <summary>PUT /api/publisher/series/{seriesId}/monetization-settings — mặc định N trang đầu xem trước mỗi chương trả phí.</summary>
    [HttpPut("series/{seriesId:guid}/monetization-settings")]
    [ProducesResponseType(typeof(SeriesMonetizationSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(
        [FromRoute] Guid seriesId, [FromBody] UpdateSeriesMonetizationSettingsRequest request, CancellationToken ct)
        => ToResult(await _mediator.Send(
            new UpdateSeriesMonetizationSettingsCommand(seriesId, request.DefaultPreviewPageCount), ct));

    /// <summary>GET /api/publisher/chapters/{chapterId}/manage — trang, trang xem trước, ảnh dịch, giá theo ngôn ngữ.</summary>
    [HttpGet("chapters/{chapterId:guid}/manage")]
    [ProducesResponseType(typeof(ChapterManageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetManage([FromRoute] Guid chapterId, CancellationToken ct)
        => ToResult(await _mediator.Send(new GetChapterManageQuery(chapterId), ct));

    /// <summary>PUT /api/publisher/chapters/{chapterId}/preview-pages — chọn trang xem trước cho người chưa mua (chương chuyển sang chọn tay).</summary>
    [HttpPut("chapters/{chapterId:guid}/preview-pages")]
    [ProducesResponseType(typeof(ChapterManageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetPreviewPages(
        [FromRoute] Guid chapterId, [FromBody] SetChapterPreviewPagesRequest request, CancellationToken ct)
        => ToResult(await _mediator.Send(new SetChapterPreviewPagesCommand(chapterId, request.PreviewPageIds ?? []), ct));

    /// <summary>DELETE /api/publisher/chapters/{chapterId}/preview-pages — bỏ chọn tay, quay về mặc định của bộ truyện.</summary>
    [HttpDelete("chapters/{chapterId:guid}/preview-pages")]
    [ProducesResponseType(typeof(ChapterManageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPreviewPages([FromRoute] Guid chapterId, CancellationToken ct)
        => ToResult(await _mediator.Send(new ResetChapterPreviewPagesCommand(chapterId), ct));

    /// <summary>PUT /api/publisher/chapters/{chapterId}/language-prices — giá chương trả phí theo từng ngôn ngữ.</summary>
    [HttpPut("chapters/{chapterId:guid}/language-prices")]
    [ProducesResponseType(typeof(ChapterManageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetLanguagePrices(
        [FromRoute] Guid chapterId, [FromBody] SetChapterLanguagePricesRequest request, CancellationToken ct)
        => ToResult(await _mediator.Send(new SetChapterLanguagePricesCommand(chapterId, request.Prices ?? []), ct));

    /// <summary>PUT /api/publisher/chapters/{chapterId}/pages/{pageId}/translations/{language} — upload ảnh trang bản dịch (en/ja).</summary>
    [HttpPut("chapters/{chapterId:guid}/pages/{pageId:guid}/translations/{language}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SetPageTranslationCommandHandler.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = SetPageTranslationCommandHandler.MaxBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(ChapterManageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadTranslation(
        [FromRoute] Guid chapterId, [FromRoute] Guid pageId, [FromRoute] string language, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Thiếu file ảnh trang." });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return ToResult(await _mediator.Send(
            new SetPageTranslationCommand(chapterId, pageId, language, buffer.ToArray(), CurrentUserId), ct));
    }

    /// <summary>DELETE /api/publisher/chapters/{chapterId}/pages/{pageId}/translations/{language} — xóa ảnh dịch.</summary>
    [HttpDelete("chapters/{chapterId:guid}/pages/{pageId:guid}/translations/{language}")]
    [ProducesResponseType(typeof(ChapterManageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTranslation(
        [FromRoute] Guid chapterId, [FromRoute] Guid pageId, [FromRoute] string language, CancellationToken ct)
        => ToResult(await _mediator.Send(new SetPageTranslationCommand(chapterId, pageId, language, null, CurrentUserId), ct));

    /// <summary>GET /api/publisher/chapters/{chapterId}/pages/{pageId}/image?lang= — ảnh xem trước (cả chương chưa phát hành).</summary>
    [HttpGet("chapters/{chapterId:guid}/pages/{pageId:guid}/image")]
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
