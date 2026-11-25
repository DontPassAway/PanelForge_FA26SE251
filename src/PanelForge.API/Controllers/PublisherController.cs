using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Models;
using PanelForge.Application.Features.Publisher.Models;
using PanelForge.Application.Features.Publisher.Queries.GetPublisherRevenue;
using PanelForge.Application.Features.Publisher.Queries.GetPublisherChapters;
using PanelForge.Application.Features.Publisher.Queries.GetPublisherSeries;
using PanelForge.Application.Features.Publisher.Queries.GetPublisherSeriesDetail;
using PanelForge.Application.Features.Publisher.Queries.GetPublisherWorkspaces;

namespace PanelForge.API.Controllers;

[ApiController]
[Authorize(Roles = "Publisher,Admin")]
[Route("api/publisher")]
public sealed class PublisherController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublisherController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/publisher/chapters
    /// Danh sách chương toàn hệ thống cho Publisher Portal (lọc theo status, workspaceId, seriesId, search).
    /// </summary>
    [HttpGet("chapters")]
    [ProducesResponseType(typeof(PagedResult<PublisherChapterDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChapters(
        [FromQuery] string? status,
        [FromQuery] Guid? workspaceId,
        [FromQuery] Guid? seriesId,
        [FromQuery] string? search,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPublisherChaptersQuery(
            Status: status,
            WorkspaceId: workspaceId,
            SeriesId: seriesId,
            Search: search,
            PageIndex: pageIndex,
            PageSize: pageSize
        ), ct);

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/publisher/series
    /// Danh sách Series toàn hệ thống kèm số lượng chương chờ duyệt xuất bản và đã phát hành.
    /// Hỗ trợ filter: needs_review, scheduled, all; workspaceId, search, sortBy (az, latest).
    /// </summary>
    [HttpGet("series")]
    [ProducesResponseType(typeof(IReadOnlyList<PublisherSeriesListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSeries(
        [FromQuery] string? filter,
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPublisherSeriesQuery(
            Filter: filter,
            WorkspaceId: workspaceId,
            Search: search,
            SortBy: sortBy
        ), ct);

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/publisher/series/{seriesId}
    /// Chi tiết bộ truyện & danh sách chương (Approved / Published) dành cho Publisher.
    /// </summary>
    [HttpGet("series/{seriesId:guid}")]
    [ProducesResponseType(typeof(PublisherSeriesDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeriesDetail(
        [FromRoute] Guid seriesId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPublisherSeriesDetailQuery(seriesId), ct);
        if (!result.IsSuccess)
        {
            return result.ErrorCode == ResultErrorCodes.NotFound
                ? NotFound(new { message = result.ErrorMessage })
                : BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/publisher/revenue
    /// Doanh thu theo bộ truyện (mở khóa chương + trang trả phí) kèm lượt đọc; lọc theo Studio / tên truyện.
    /// Giao dịch chi tiết và duyệt VietQR: /api/workspaces/{workspaceId}/payments (Publisher/Admin).
    /// </summary>
    [HttpGet("revenue")]
    [ProducesResponseType(typeof(PublisherRevenueDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRevenue(
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPublisherRevenueQuery(workspaceId, search), ct);
        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/publisher/workspaces
    /// Danh sách Studio Workspaces toàn hệ thống cho dropdown bộ lọc của Publisher.
    /// </summary>
    [HttpGet("workspaces")]
    [ProducesResponseType(typeof(IReadOnlyList<PublisherWorkspaceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspaces(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPublisherWorkspacesQuery(), ct);
        return Ok(result.Value);
    }
}
