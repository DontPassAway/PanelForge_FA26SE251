using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Features.Elements.AddElement;
using PanelForge.Application.Features.Elements.GetPageSnapshot;
using PanelForge.Application.Features.Elements.MoveElement;
using PanelForge.Application.Features.Elements.RemoveElement;
using System.Security.Claims;

namespace PanelForge.API.Controllers;

/// <summary>
/// Controller xử lý CRUD cho Elements trên Page.
///
/// ⚠️  Không có SQL INSERT/UPDATE/DELETE trong toàn bộ luồng này.
/// Mọi thao tác đều: Command → MediatR → Domain Method → Domain Event → Marten APPEND.
/// </summary>
[ApiController]
[Route("api/pages/{pageId:guid}/elements")]
[Authorize]
public sealed class ElementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ElementsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // ── GET — Lấy toàn bộ elements của Page (Read side) ─────────────────────

    /// <summary>
    /// GET /api/pages/{pageId}/elements
    /// GET /api/pages/{pageId}/elements?includeRemoved=true
    ///
    /// Trả về snapshot hiện tại của Page với tất cả Elements.
    /// Read Model được rebuild bằng cách replay event stream.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PageSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPageSnapshot(
        Guid pageId,
        [FromQuery] bool includeRemoved = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetPageSnapshotQuery(pageId, includeRemoved),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound(new { message = result.ErrorMessage });
    }

    // ── POST — Thêm Element (Create → ElementAddedEvent APPEND) ─────────────

    /// <summary>
    /// POST /api/pages/{pageId}/elements
    ///
    /// Body:
    /// {
    ///   "elementType": "DialogueBalloon",
    ///   "x": 50, "y": 80, "width": 200, "height": 120,
    ///   "zIndex": 2,
    ///   "content": "Tôi sẽ không bao giờ từ bỏ!"
    /// }
    ///
    /// Thao tác SQL duy nhất xảy ra:
    ///   INSERT INTO mt_events (stream_id, type, data) VALUES (pageId, 'element_added', '{...}')
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AddElementResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddElement(
        Guid pageId,
        [FromBody] AddElementRequest request,
        CancellationToken cancellationToken)
    {
        var command = new AddElementCommand(
            PageId:        pageId,
            ElementType:   request.ElementType,
            X:             request.X,
            Y:             request.Y,
            Width:         request.Width,
            Height:        request.Height,
            ZIndex:        request.ZIndex,
            Content:       request.Content,
            AssetId:       request.AssetId,
            AddedByUserId: GetCurrentUserId()
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return result.ErrorMessage!.Contains("không tồn tại")
                ? NotFound(new { message = result.ErrorMessage })
                : BadRequest(new { message = result.ErrorMessage });

        // 202 Accepted — Event đã được append thành công
        return Accepted(result.Value);
    }

    // ── PUT — Di chuyển / Resize Element (Update → ElementMovedEvent APPEND) ─

    /// <summary>
    /// PUT /api/pages/{pageId}/elements/{elementId}/position
    ///
    /// Body:
    /// {
    ///   "newX": 200, "newY": 400,
    ///   "newWidth": 300, "newHeight": 450,
    ///   "expectedPageVersion": 5
    /// }
    ///
    /// KHÔNG UPDATE row nào.
    /// APPEND ElementMovedEvent vào stream với vị trí cũ và mới (audit trail).
    /// </summary>
    [HttpPut("{elementId:guid}/position")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MoveElement(
        Guid pageId,
        Guid elementId,
        [FromBody] MoveElementRequest request,
        CancellationToken cancellationToken)
    {
        var command = new MoveElementCommand(
            PageId:              pageId,
            ElementId:           elementId,
            NewX:                request.NewX,
            NewY:                request.NewY,
            NewWidth:            request.NewWidth,
            NewHeight:           request.NewHeight,
            ExpectedPageVersion: request.ExpectedPageVersion,
            MovedByUserId:       GetCurrentUserId()
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.ErrorMessage!.Contains("Xung đột"))
                return Conflict(new { message = result.ErrorMessage });
            if (result.ErrorMessage.Contains("không tồn tại"))
                return NotFound(new { message = result.ErrorMessage });
            return BadRequest(new { message = result.ErrorMessage });
        }

        return Accepted(new { message = "Element đã được di chuyển thành công." });
    }

    // ── DELETE — Xóa mềm Element (Soft Delete → ElementRemovedEvent APPEND) ──

    /// <summary>
    /// DELETE /api/pages/{pageId}/elements/{elementId}?reason=USER_DELETE
    ///
    /// KHÔNG xóa row nào khỏi database.
    /// APPEND ElementRemovedEvent vào stream.
    /// Element vẫn tồn tại trong lịch sử và có thể Restore sau này.
    ///
    /// Để xem Element đã "xóa": GET /elements?includeRemoved=true
    /// </summary>
    [HttpDelete("{elementId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveElement(
        Guid pageId,
        Guid elementId,
        [FromQuery] string reason = "USER_DELETE",
        CancellationToken cancellationToken = default)
    {
        var command = new RemoveElementCommand(
            PageId:          pageId,
            ElementId:       elementId,
            Reason:          reason,
            RemovedByUserId: GetCurrentUserId()
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return result.ErrorMessage!.Contains("không tồn tại") ||
                   result.ErrorMessage.Contains("đã bị xóa")
                ? NotFound(new { message = result.ErrorMessage })
                : BadRequest(new { message = result.ErrorMessage });

        return Accepted(new
        {
            message    = "Element đã được xóa mềm. Dữ liệu lịch sử vẫn được bảo toàn.",
            elementId,
            canRestore = true  // Client biết rằng có thể Restore sau này
        });
    }

    // ── Helper ─────────────────────────────────────────────────────────────
    private Guid GetCurrentUserId()
    {
        var str = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub")
               ?? Guid.Empty.ToString();
        return Guid.TryParse(str, out var id) ? id : Guid.Empty;
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public sealed record AddElementRequest(
    string ElementType,
    double X,
    double Y,
    double Width,
    double Height,
    int ZIndex,
    string? Content,
    string? AssetId
);

public sealed record MoveElementRequest(
    double NewX,
    double NewY,
    double NewWidth,
    double NewHeight,
    long ExpectedPageVersion  // Optimistic Concurrency token
);
