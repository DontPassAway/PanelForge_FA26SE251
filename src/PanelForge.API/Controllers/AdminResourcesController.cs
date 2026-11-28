using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Resources.Commands.ReviewResourceRequest;
using PanelForge.Application.Features.Resources.Commands.UpdateResourcePolicy;
using PanelForge.Application.Features.Resources.Commands.UpdateStudioResources;
using PanelForge.Application.Features.Resources.Models;
using PanelForge.Application.Features.Resources.Queries.GetResourceOverview;
using PanelForge.Application.Features.Resources.Queries.GetResourcePolicy;
using PanelForge.Application.Features.Resources.Queries.GetResourceRequests;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>
/// Quản lý tài nguyên cấp nền tảng (Resource Allocation): chính sách mặc định, cấp phát Storage/AI theo Studio,
/// duyệt yêu cầu tăng tài nguyên. Chỉ Administrator. Cấp phát, không mua bán (không B2B billing).
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminResourcesController : ControllerBase
{
    private readonly IMediator _mediator;
    public AdminResourcesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/admin/resource-policy</summary>
    [HttpGet("resource-policy")]
    [ProducesResponseType(typeof(ResourcePolicyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPolicy(CancellationToken ct)
        => ToResult(await _mediator.Send(new GetResourcePolicyQuery(), ct));

    /// <summary>PUT /api/admin/resource-policy — chỉ áp cho Studio tạo sau đó.</summary>
    [HttpPut("resource-policy")]
    [ProducesResponseType(typeof(ResourcePolicyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePolicy([FromBody] UpdateResourcePolicyRequest request, CancellationToken ct)
        => ToResult(await _mediator.Send(new UpdateResourcePolicyCommand(request), ct));

    /// <summary>GET /api/admin/resources — mức dùng/cấp phát Storage + AI của mọi Studio.</summary>
    [HttpGet("resources")]
    [ProducesResponseType(typeof(ResourceOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
        => ToResult(await _mediator.Send(new GetResourceOverviewQuery(), ct));

    /// <summary>PUT /api/admin/workspaces/{id}/resources — chỉnh cấp phát một Studio (bắt buộc lý do).</summary>
    [HttpPut("workspaces/{id:guid}/resources")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateStudioResources(Guid id, [FromBody] UpdateStudioResourcesRequest request, CancellationToken ct)
    {
        if (AdminId() is not { } adminId) return Unauthorized();
        var result = await _mediator.Send(new UpdateStudioResourcesCommand(id, adminId, request), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    /// <summary>GET /api/admin/resource-requests?status=&amp;resourceType=&amp;search=</summary>
    [HttpGet("resource-requests")]
    [ProducesResponseType(typeof(IReadOnlyList<ResourceRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRequests(
        [FromQuery] ResourceRequestStatus? status, [FromQuery] ResourceType? resourceType, [FromQuery] string? search,
        CancellationToken ct)
        => ToResult(await _mediator.Send(new GetResourceRequestsQuery(null, status, resourceType, search), ct));

    /// <summary>POST /api/admin/resource-requests/{id}/approve</summary>
    [HttpPost("resource-requests/{id:guid}/approve")]
    public Task<IActionResult> Approve(Guid id, [FromBody] ReviewResourceRequestRequest? request, CancellationToken ct)
        => Review(id, true, request?.Note, ct);

    /// <summary>POST /api/admin/resource-requests/{id}/reject — bắt buộc note.</summary>
    [HttpPost("resource-requests/{id:guid}/reject")]
    public Task<IActionResult> Reject(Guid id, [FromBody] ReviewResourceRequestRequest? request, CancellationToken ct)
        => Review(id, false, request?.Note, ct);

    private async Task<IActionResult> Review(Guid id, bool approve, string? note, CancellationToken ct)
    {
        if (AdminId() is not { } adminId) return Unauthorized();
        var result = await _mediator.Send(new ReviewResourceRequestCommand(id, adminId, approve, note), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    private Guid? AdminId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;

    private IActionResult ToResult<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : Error(result);

    private IActionResult Error<T>(Result<T> result) => result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { message = result.ErrorMessage }),
        ResultErrorCodes.Conflict => Conflict(new { message = result.ErrorMessage, code = result.ErrorCode }),
        _ => BadRequest(new { message = result.ErrorMessage })
    };
}
