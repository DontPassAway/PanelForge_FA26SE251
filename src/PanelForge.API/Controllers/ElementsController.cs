using PanelForge.Application.Features.CanvasElements.Commands.UpdateElementStyle;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.CanvasElements.Commands.AcknowledgeOverflow;
using PanelForge.Application.Features.CanvasElements.Commands.AddElement;
using PanelForge.Application.Features.CanvasElements.Commands.SetReadingOrder;
using PanelForge.Application.Features.CanvasElements.Commands.UpdateElementText;
using PanelForge.Domain.Enums;
using PanelForge.Application.Features.CanvasElements.Queries.GetPageSnapshot;
using PanelForge.Application.Features.CanvasElements.Commands.MoveElement;
using PanelForge.Application.Features.CanvasElements.Commands.RemoveElement;
using PanelForge.Domain.Entities.Content;
using System.Security.Claims;
using PanelForge.API.Common;

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
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(PageSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPageSnapshot(
        Guid pageId,
        [FromQuery] bool includeRemoved = false,
        [FromQuery] long? version = null,
        CancellationToken cancellationToken = default)
    {
        if (version is < 1)
            return BadRequest(new { message = "version phải ≥ 1." });

        var result = await _mediator.Send(
            new GetPageSnapshotQuery(pageId, includeRemoved, version),
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
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist, WorkspaceRole.Letterer)]
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
            AddedByUserId: GetCurrentUserId(),
            ScriptLineId:  request.ScriptLineId,
            ClientElementId: request.ClientElementId,
            PanelId:       request.PanelId,
            Rotation:      request.Rotation,
            BibleEntryId:  request.BibleEntryId
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess) return Failure(result.ErrorMessage, result.ErrorCode);

        // 202 Accepted — Event đã được append thành công
        return Accepted(result.Value);
    }

    // ── PUT — Sửa chữ / làm mới chữ lỗi thời / gắn lại ScriptLine (BR-05) ───

    /// <summary>
    /// PUT /api/pages/{pageId}/elements/{elementId}/text
    /// Body: { "content": "chữ mới (bỏ trống = nguyên văn dòng thoại)", "scriptLineId": "guid? (bỏ trống = giữ dòng hiện tại)" }
    /// </summary>
    [HttpPut("{elementId:guid}/text")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Letterer)]
    [ProducesResponseType(typeof(UpdateElementTextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateElementText(
        Guid pageId, Guid elementId, [FromBody] UpdateElementTextRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateElementTextCommand(pageId, elementId, request.Content, request.ScriptLineId, GetCurrentUserId(), request.ExpectedElementVersion),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.ErrorMessage, result.ErrorCode);
    }

    // ── POST — Xác nhận cảnh báo tràn chữ kèm lý do (BR-20) ─────────────────

    /// <summary>POST /api/pages/{pageId}/elements/{elementId}/overflow-ack — Body: { "reason": "..." } (bắt buộc, ≤ 500 ký tự)</summary>
    [HttpPost("{elementId:guid}/overflow-ack")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Letterer)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcknowledgeOverflow(
        Guid pageId, Guid elementId, [FromBody] AcknowledgeOverflowRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AcknowledgeOverflowCommand(pageId, elementId, request.Reason, GetCurrentUserId()), cancellationToken);
        return result.IsSuccess ? Accepted(new { message = "Đã xác nhận cảnh báo tràn chữ.", elementVersion = result.Value }) : Failure(result.ErrorMessage, result.ErrorCode);
    }

    // ── PUT — Ghi đè thứ tự đọc (Letterer) ───────────────────────────────────

    /// <summary>PUT /api/pages/{pageId}/elements/{elementId}/reading-order — Body: { "readingOrder": 3 } hoặc null để bỏ ghi đè</summary>
    [HttpPut("{elementId:guid}/reading-order")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Letterer)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetReadingOrder(
        Guid pageId, Guid elementId, [FromBody] SetReadingOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SetReadingOrderCommand(pageId, elementId, request.ReadingOrder, GetCurrentUserId(), request.ExpectedElementVersion), cancellationToken);
        return result.IsSuccess ? Accepted(new { message = "Đã cập nhật thứ tự đọc.", elementVersion = result.Value }) : Failure(result.ErrorMessage, result.ErrorCode);
    }

    // ── PUT — Kiểu chữ (Letterer) ─────────────────────────────────────────────

    /// <summary>
    /// PUT /api/pages/{pageId}/elements/{elementId}/style — Body: { "style": { fontFamily, fontSize, bold, italic, align, color,
    /// strokeColor, strokeWidth, lineHeight, preset } | null, "expectedElementVersion": 3 }. null = về kiểu mặc định.
    /// </summary>
    [HttpPut("{elementId:guid}/style")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Letterer)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStyle(
        Guid pageId, Guid elementId, [FromBody] UpdateElementStyleRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateElementStyleCommand(pageId, elementId, request.Style, GetCurrentUserId(), request.ExpectedElementVersion),
            cancellationToken);
        return result.IsSuccess ? Accepted(new { message = "Đã cập nhật kiểu chữ.", elementVersion = result.Value }) : Failure(result.ErrorMessage, result.ErrorCode);
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
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist, WorkspaceRole.Letterer)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status202Accepted)]
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
            MovedByUserId:       GetCurrentUserId(),
            ExpectedElementVersion: request.ExpectedElementVersion,
            NewRotation:         request.NewRotation,
            NewZIndex:           request.NewZIndex
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess) return Failure(result.ErrorMessage, result.ErrorCode);

        return Accepted(new { message = "Element đã được di chuyển thành công.", elementVersion = result.Value });
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
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist, WorkspaceRole.Letterer)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveElement(
        Guid pageId,
        Guid elementId,
        [FromQuery] string reason = "USER_DELETE",
        [FromQuery] int? expectedElementVersion = null,
        CancellationToken cancellationToken = default)
    {
        var command = new RemoveElementCommand(
            PageId:          pageId,
            ElementId:       elementId,
            Reason:          reason,
            RemovedByUserId: GetCurrentUserId(),
            ExpectedElementVersion: expectedElementVersion
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return result.ErrorMessage!.Contains("đã bị xóa") && !result.ErrorMessage.Contains("Xung đột")
                ? NotFound(new { message = result.ErrorMessage })
                : Failure(result.ErrorMessage, result.ErrorCode);

        return Accepted(new
        {
            message    = "Element đã được xóa mềm. Dữ liệu lịch sử vẫn được bảo toàn.",
            elementId,
            canRestore = true  // Client biết rằng có thể Restore sau này
        });
    }

    // ── Helper ─────────────────────────────────────────────────────────────
    private IActionResult Failure(string? message, string? code)
    {
        message ??= "Yêu cầu không hợp lệ.";
        if (code == ResultErrorCodes.ChapterReadOnly) // BR-12, không có field conflict nên FE không mở hộp thoại tải lại
            return Conflict(new { message, code });
        if (message.Contains(Page.DuplicateBindingCode))
            return Conflict(new { message, code = Page.DuplicateBindingCode });
        if (message.Contains(Page.ElementVersionConflictCode))
            return Conflict(new { message, code = "CONFLICT", conflict = Page.ElementVersionConflictCode });
        if (message.Contains("Xung đột"))
            return Conflict(new { message, code = "CONFLICT" });
        return message.Contains("không tồn tại")
            ? NotFound(new { message })
            : BadRequest(new { message });
    }

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
    string? AssetId,
    Guid? ScriptLineId = null,  // BR-05: chỉ cho SPEECH_BALLOON | THOUGHT_BALLOON | NARRATION_BOX | SOUND_EFFECT
    Guid? ClientElementId = null,  // NFR-09: id client tự sinh (UUID v4) để gửi lại an toàn sau mất mạng
    Guid? PanelId = null,          // Panel chứa element ảnh (nhân vật, bối cảnh…)
    double Rotation = 0,           // góc xoay, độ
    Guid? BibleEntryId = null      // mục Series Bible element thể hiện
);

public sealed record UpdateElementTextRequest(string? Content, Guid? ScriptLineId, int? ExpectedElementVersion = null);

public sealed record AcknowledgeOverflowRequest(string Reason);

public sealed record SetReadingOrderRequest(int? ReadingOrder, int? ExpectedElementVersion = null);
public sealed record UpdateElementStyleRequest(TextStyle? Style, int? ExpectedElementVersion = null);

public sealed record MoveElementRequest(
    double NewX,
    double NewY,
    double NewWidth,
    double NewHeight,
    long? ExpectedPageVersion = null,   // cách cũ: xung đột theo cả Page
    int? ExpectedElementVersion = null, // BR-11: version element trong GET /elements; chỉ xung đột khi cùng element
    double? NewRotation = null,         // null = giữ nguyên
    int? NewZIndex = null               // null = giữ nguyên thứ tự lớp
);
