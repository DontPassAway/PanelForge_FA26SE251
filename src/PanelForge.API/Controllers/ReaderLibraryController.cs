using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Users.Library;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>
/// Thư viện cá nhân của độc giả (Mobile): Đọc sau, Theo dõi, Yêu thích và Lịch sử đọc, đồng bộ giữa các thiết bị.
/// Chỉ thao tác trên dữ liệu của chính người gọi nên không cần quyền theo Workspace.
/// </summary>
[ApiController]
[Authorize]
[Route("api/users/me/library")]
public sealed class ReaderLibraryController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReaderLibraryController(IMediator mediator) => _mediator = mediator;

    private Guid? CallerId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private static ReaderLibraryList? ParseList(string list) => list.ToLowerInvariant() switch
    {
        "read-later" => ReaderLibraryList.ReadLater,
        "following" => ReaderLibraryList.Following,
        "favorites" => ReaderLibraryList.Favorite,
        _ => null,
    };

    private IActionResult FromResult<T>(Result<T> result, Func<T, IActionResult> ok) =>
        result.IsSuccess ? ok(result.Value!)
        : result.ErrorCode == ResultErrorCodes.NotFound ? NotFound(new { message = result.ErrorMessage })
        : BadRequest(new { message = result.ErrorMessage });

    /// <summary>GET /api/users/me/library — toàn bộ thư viện của người đang đăng nhập.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ReaderLibraryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (CallerId() is not { } userId) return Unauthorized();
        return FromResult(await _mediator.Send(new GetMyLibraryQuery(userId), ct), Ok);
    }

    /// <summary>POST /api/users/me/library/merge — gộp dữ liệu trên thiết bị vào server, trả về thư viện sau khi gộp.</summary>
    [HttpPost("merge")]
    [ProducesResponseType(typeof(ReaderLibraryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Merge([FromBody] MergeReaderLibraryRequest request, CancellationToken ct)
    {
        if (CallerId() is not { } userId) return Unauthorized();
        return FromResult(await _mediator.Send(new MergeMyLibraryCommand(userId, request), ct), Ok);
    }

    /// <summary>PUT /api/users/me/library/{list}/{seriesId} — thêm vào read-later | following | favorites.
    /// Với following, body <c>{ lastSeenAt }</c> (tuỳ chọn) cập nhật mốc đã xem chương mới.</summary>
    [HttpPut("{list}/{seriesId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Add(string list, Guid seriesId, [FromBody] MarkFollowSeenRequest? body, CancellationToken ct)
    {
        if (CallerId() is not { } userId) return Unauthorized();
        if (ParseList(list) is not { } kind) return NotFound(new { message = "Danh sách không tồn tại." });
        var result = await _mediator.Send(new SetLibraryItemCommand(userId, kind, seriesId, true, body?.LastSeenAt), ct);
        return FromResult(result, _ => NoContent());
    }

    /// <summary>DELETE /api/users/me/library/{list}/{seriesId} — bỏ khỏi danh sách (không có cũng trả 204).</summary>
    [HttpDelete("{list}/{seriesId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(string list, Guid seriesId, CancellationToken ct)
    {
        if (CallerId() is not { } userId) return Unauthorized();
        if (ParseList(list) is not { } kind) return NotFound(new { message = "Danh sách không tồn tại." });
        var result = await _mediator.Send(new SetLibraryItemCommand(userId, kind, seriesId, false), ct);
        return FromResult(result, _ => NoContent());
    }

    /// <summary>PUT /api/users/me/library/history — ghi tiến độ đọc (bản đọc sau cùng thắng).</summary>
    [HttpPut("history")]
    [ProducesResponseType(typeof(ReadingProgressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveProgress([FromBody] ReadingProgressDto progress, CancellationToken ct)
    {
        if (CallerId() is not { } userId) return Unauthorized();
        return FromResult(await _mediator.Send(new SaveReadingProgressCommand(userId, progress), ct), Ok);
    }

    /// <summary>DELETE /api/users/me/library/history[?seriesId=] — xoá lịch sử đọc (một bộ hoặc tất cả).</summary>
    [HttpDelete("history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearHistory([FromQuery] Guid? seriesId, CancellationToken ct)
    {
        if (CallerId() is not { } userId) return Unauthorized();
        var result = await _mediator.Send(new ClearReadingHistoryCommand(userId, seriesId), ct);
        return FromResult(result, removed => Ok(new { removed }));
    }
}
