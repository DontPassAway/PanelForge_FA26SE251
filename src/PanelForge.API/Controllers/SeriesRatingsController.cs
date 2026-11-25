using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Publisher.Monetization;

namespace PanelForge.API.Controllers;

/// <summary>
/// Độc giả đánh giá 1–5 sao cho bộ truyện (Mobile). Tách khỏi <see cref="CatalogController"/> vì controller đó
/// <c>[AllowAnonymous]</c> — đánh giá bắt buộc đăng nhập. Điểm trung bình hiển thị trong GET /api/catalog/series/{id}.
/// </summary>
[ApiController]
[Authorize]
[Route("api/catalog/series/{seriesId:guid}/rating")]
public sealed class SeriesRatingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SeriesRatingsController(IMediator mediator) => _mediator = mediator;

    /// <summary>PUT /api/catalog/series/{seriesId}/rating — tạo hoặc sửa đánh giá của người đang đăng nhập.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(SeriesRatingSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rate([FromRoute] Guid seriesId, [FromBody] RateSeriesRequest request, CancellationToken ct)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(claim, out var userId)) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new RateSeriesCommand(seriesId, userId, request.Stars, request.Language), ct);
        if (result.IsSuccess) return Ok(result.Value);
        return result.ErrorCode == ResultErrorCodes.NotFound
            ? NotFound(new { message = result.ErrorMessage })
            : BadRequest(new { message = result.ErrorMessage });
    }
}
