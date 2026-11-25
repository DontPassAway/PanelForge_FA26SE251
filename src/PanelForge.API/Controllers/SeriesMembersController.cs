using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesMembers.Commands.AddSeriesMember;
using PanelForge.Application.Features.SeriesMembers.Commands.RemoveSeriesMember;
using PanelForge.Application.Features.SeriesMembers.Commands.UpdateSeriesMemberRole;
using PanelForge.Application.Features.SeriesMembers.Models;
using PanelForge.Application.Features.SeriesMembers.Queries.GetSeriesCandidates;
using PanelForge.Application.Features.SeriesMembers.Queries.GetSeriesMembers;

namespace PanelForge.API.Controllers;

[ApiController]
[Authorize]
[Route("api/series/{seriesId:guid}/members")]
public sealed class SeriesMembersController : ControllerBase
{
    private readonly IMediator _mediator;

    public SeriesMembersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// GET /api/series/{seriesId}/members/candidates
    /// Lấy danh sách thành viên trong Workspace để đưa lên khung/modal chọn thêm vào Series.
    /// </summary>
    [HttpGet("candidates")]
    [ProducesResponseType(typeof(IReadOnlyList<SeriesCandidateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeriesCandidates(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetSeriesCandidatesQuery(GetCurrentUserId(), seriesId),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// GET /api/series/{seriesId}/members
    /// Lấy danh sách thành viên được chỉ định tham gia vào Series.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SeriesMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeriesMembers(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetSeriesMembersQuery(GetCurrentUserId(), seriesId),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/members
    /// Chỉ định một thành viên trong Workspace vào Series với vai trò cụ thể.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SeriesMemberDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddSeriesMember(
        [FromRoute] Guid seriesId,
        [FromBody] AddSeriesMemberRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AddSeriesMemberCommand(GetCurrentUserId(), seriesId, request.UserId, request.Role),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : ToError(result);
    }

    /// <summary>
    /// PUT /api/series/{seriesId}/members/{userId}
    /// Thay đổi vai trò của thành viên trong Series.
    /// </summary>
    [HttpPut("{userId:guid}")]
    [ProducesResponseType(typeof(SeriesMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSeriesMemberRole(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid userId,
        [FromBody] UpdateSeriesMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateSeriesMemberRoleCommand(GetCurrentUserId(), seriesId, userId, request.Role),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// DELETE /api/series/{seriesId}/members/{userId}
    /// Rút thành viên khỏi dự án Series.
    /// </summary>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveSeriesMember(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RemoveSeriesMemberCommand(GetCurrentUserId(), seriesId, userId),
            cancellationToken);

        return result.IsSuccess ? NoContent() : ToError(result);
    }

    private IActionResult ToError<T>(Result<T> result)
    {
        var body = new { message = result.ErrorMessage };
        return result.ErrorCode switch
        {
            ResultErrorCodes.Unauthorized => Unauthorized(body),
            ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            ResultErrorCodes.NotFound => NotFound(body),
            ResultErrorCodes.Conflict => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
