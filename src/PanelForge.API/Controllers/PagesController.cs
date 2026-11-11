using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common;
using PanelForge.API.Common.Attributes;
using PanelForge.Domain.Enums;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Pages.Models;
using PanelForge.Application.Features.Pages.Commands.CreatePage;
using PanelForge.Application.Features.Pages.Commands.DeletePage;
using PanelForge.Application.Features.Pages.Queries.GetPageById;
using PanelForge.Application.Features.Pages.Queries.GetPageHierarchy;
using PanelForge.Application.Features.Pages.Queries.GetPagesByChapter;
using PanelForge.Application.Features.Pages.Commands.ReorderPage;
using PanelForge.Application.Features.Pages.Commands.TogglePageContentReady;
using PanelForge.Application.Features.Pages.Commands.UpdateCanvasSettings;
using PanelForge.Application.Common;
using PanelForge.Application.Features.CanvasElements.Queries.GetPageSnapshot;
using PanelForge.Application.Features.Pages.Commands.RestorePageVersion;
using PanelForge.Application.Features.Pages.Commands.SyncScriptStructure;
using PanelForge.Application.Features.Pages.Queries.ComparePageVersions;
using PanelForge.Application.Features.Pages.Queries.SplitArtwork;
using PanelForge.Application.Features.Pages.Queries.GetPageHistory;
using PanelForge.Application.Features.Consistency.Commands.CheckPageVisualContinuity;

namespace PanelForge.API.Controllers;

[ApiController]
[Authorize]
public sealed class PagesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PagesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/pages
    /// Tạo một trang truyện mới trong chương.
    /// Khởi tạo Stream mới trên Marten Event Store và lưu Read Model vào EF Core.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/pages")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(PageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePage(
        [FromRoute] Guid chapterId,
        [FromBody] CreatePageRequestBody body,
        CancellationToken cancellationToken)
    {
        var command = new CreatePageCommand(
            ChapterId:    chapterId,
            PageNumber:   body.PageNumber,
            LayoutFormat: body.LayoutFormat ?? "StandardPage",
            WidthPx:      body.WidthPx > 0 ? body.WidthPx : 1200,
            HeightPx:     body.HeightPx > 0 ? body.HeightPx : 1800,
            Dpi:          body.Dpi > 0 ? body.Dpi : 300,
            UserId:       GetCurrentUserId()
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return this.ConflictOrNull(result) ?? BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(
            nameof(GetPageById),
            new { pageId = result.Value!.Id },
            result.Value
        );
    }

    /// <summary>
    /// PUT /api/chapters/{chapterId}/script-structure
    /// Lưu khung Trang → Panel → dòng kịch bản từ trình soạn script trong một transaction. Không xoá Page/Panel.
    /// </summary>
    [HttpPut("api/chapters/{chapterId:guid}/script-structure")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(SyncScriptStructureResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SyncScriptStructure(
        [FromRoute] Guid chapterId,
        [FromBody] SyncScriptStructureRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SyncScriptStructureCommand(chapterId, GetCurrentUserId(), body.Pages ?? []), cancellationToken);

        if (result.IsSuccess) return Ok(result.Value);
        if (result.ErrorCode == ResultErrorCodes.NotFound) return NotFound(new { error = result.ErrorMessage });
        return this.ConflictOrNull(result) ?? BadRequest(new { error = result.ErrorMessage });
    }

    public sealed record SyncScriptStructureRequest(IReadOnlyList<ScriptStructurePage>? Pages);

    /// <summary>
    /// GET /api/chapters/{chapterId}/pages
    /// Lấy danh sách tất cả các trang của một chương, sắp xếp theo thứ tự trang.
    /// </summary>
    [HttpGet("api/chapters/{chapterId:guid}/pages")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<PageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPagesByChapter(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken)
    {
        var query = new GetPagesByChapterQuery(chapterId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/pages/{pageId}
    /// Lấy thông tin chi tiết của một trang (rebuilt từ Marten Event Store).
    /// </summary>
    [HttpGet("api/pages/{pageId:guid}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(PageDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPageById(
        [FromRoute] Guid pageId,
        CancellationToken cancellationToken)
    {
        var query = new GetPageByIdQuery(pageId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/pages/{pageId}/hierarchy
    /// Lấy toàn bộ cấu trúc phân cấp của trang bao gồm Panels và Elements bên trong.
    /// </summary>
    [HttpGet("api/pages/{pageId:guid}/hierarchy")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(PageContentHierarchyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPageHierarchy(
        [FromRoute] Guid pageId,
        CancellationToken cancellationToken)
    {
        var query = new GetPageHierarchyQuery(pageId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/pages/{pageId}/reorder
    /// Cập nhật đổi thứ tự trang trong chương.
    /// </summary>
    [HttpPut("api/pages/{pageId:guid}/reorder")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(PageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReorderPage(
        [FromRoute] Guid pageId,
        [FromBody] ReorderPageRequest body,
        CancellationToken cancellationToken)
    {
        var command = new ReorderPageCommand(pageId, body.NewPageNumber, GetCurrentUserId());
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return this.ConflictOrNull(result) ?? BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/pages/{pageId}/canvas-settings
    /// Cập nhật thông số khổ giấy / canvas của trang.
    /// </summary>
    [HttpPut("api/pages/{pageId:guid}/canvas-settings")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCanvasSettings(
        [FromRoute] Guid pageId,
        [FromBody] UpdateCanvasRequestBody body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateCanvasSettingsCommand(
            PageId:       pageId,
            WidthPx:      body.WidthPx,
            HeightPx:     body.HeightPx,
            Dpi:          body.Dpi,
            LayoutFormat: body.LayoutFormat
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return this.ConflictOrNull(result) ?? BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new { message = "Cập nhật thông số canvas thành công." });
    }

    /// <summary>
    /// DELETE /api/pages/{pageId}
    /// Xóa mềm một trang truyện khỏi chương (Soft-delete).
    /// </summary>
    [HttpDelete("api/pages/{pageId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePage(
        [FromRoute] Guid pageId,
        CancellationToken cancellationToken)
    {
        var command = new DeletePageCommand(pageId);
        var result = await _mediator.Send(command, cancellationToken);

        // 409: trang (hoặc panel của nó) còn việc đang giao
        if (!result.IsSuccess)
            return this.ConflictOrNull(result) ?? NotFound(new { error = result.ErrorMessage });

        return Ok(new { message = $"Trang {pageId} đã được xóa thành công." });
    }

    private const long MaxSplitImageBytes = 20 * 1024 * 1024;

    /// <summary>
    /// POST /api/pages/{pageId}/split-artwork (multipart/form-data): file, rightToLeft?.
    /// Gợi ý tách panel + bong bóng thoại từ ảnh vẽ cả trang. Chỉ trả toạ độ, KHÔNG lưu gì:
    /// FE cho người dùng chỉnh/xác nhận rồi mới áp vào layout.
    /// </summary>
    [HttpPost("api/pages/{pageId:guid}/split-artwork")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxSplitImageBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxSplitImageBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(PanelSplitResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SplitArtwork(
        [FromRoute] Guid pageId,
        [FromForm] SplitArtworkForm form,
        CancellationToken cancellationToken)
    {
        if (form.File is null || form.File.Length == 0)
            return BadRequest(new { error = "Thiếu file ảnh trang." });
        if (form.File.Length > MaxSplitImageBytes)
            return BadRequest(new { error = "Ảnh vượt quá 20MB." });
        if (form.File.ContentType is not ("image/png" or "image/jpeg" or "image/webp"))
            return BadRequest(new { error = "Chỉ hỗ trợ PNG, JPEG, WEBP." });

        await using var stream = form.File.OpenReadStream();
        var result = await _mediator.Send(
            new SplitArtworkQuery(pageId, stream, form.File.FileName, form.RightToLeft), cancellationToken);

        if (result.IsSuccess) return Ok(result.Value);
        return result.ErrorCode == ResultErrorCodes.NotFound
            ? NotFound(new { error = result.ErrorMessage })
            : BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode });
    }

    // ── CF4 4.3: lịch sử, xem lại, so sánh và khôi phục canvas ────────────────

    /// <summary>GET /api/pages/{pageId}/history?page=1&amp;pageSize=50 — event của stream Page, mới nhất trước (tối đa 100/trang).</summary>
    [HttpGet("api/pages/{pageId:guid}/history")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(PageHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPageHistory(
        Guid pageId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPageHistoryQuery(pageId, page, pageSize), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HistoryError(result);
    }

    /// <summary>GET /api/pages/{pageId}/versions/{version} — canvas dựng lại tại version đó (kể cả element đã xoá nếu includeRemoved).</summary>
    [HttpGet("api/pages/{pageId:guid}/versions/{version:long}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(PageSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPageVersion(
        Guid pageId, long version, [FromQuery] bool includeRemoved = false, CancellationToken cancellationToken = default)
    {
        if (version < 1) return BadRequest(new { error = "version phải ≥ 1.", code = ResultErrorCodes.Validation });
        var result = await _mediator.Send(new GetPageSnapshotQuery(pageId, includeRemoved, version), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { error = result.ErrorMessage });
    }

    /// <summary>GET /api/pages/{pageId}/compare?left=3&amp;right=7 — khác biệt theo elementId: Added / Removed / Modified.</summary>
    [HttpGet("api/pages/{pageId:guid}/compare")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(PageVersionCompareDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ComparePageVersions(
        Guid pageId, [FromQuery] long left, [FromQuery] long right, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ComparePageVersionsQuery(pageId, left, right), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HistoryError(result);
    }

    /// <summary>
    /// POST /api/pages/{pageId}/versions/{version}/restore — Body: { "expectedPageVersion": 12 }. Thêm event đưa canvas về bản cũ,
    /// không xoá lịch sử. 409 CONFLICT khi version lệch, 409 CHAPTER_READ_ONLY khi chương không còn sản xuất.
    /// </summary>
    [HttpPost("api/pages/{pageId:guid}/versions/{version:long}/restore")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(RestorePageVersionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestorePageVersion(
        Guid pageId, long version, [FromBody] RestorePageVersionRequest body, CancellationToken cancellationToken)
    {
        if (body.ExpectedPageVersion is not { } expected)
            return BadRequest(new { error = "Phải gửi expectedPageVersion.", code = ResultErrorCodes.Validation });
        var result = await _mediator.Send(
            new RestorePageVersionCommand(pageId, version, expected, GetCurrentUserId()), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HistoryError(result);
    }

    /// <summary>
    /// POST /api/pages/{pageId}/visual-continuity — Editor/Producer so ảnh từng panel của trang với character sheet đã khoá.
    /// Chỉ cảnh báo (finding Warning), không chặn duyệt chương. 503 AI_UNAVAILABLE khi không panel nào kiểm được vì lỗi AI.
    /// </summary>
    [HttpPost("api/pages/{pageId:guid}/visual-continuity")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(PageVisualContinuityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CheckPageVisualContinuity(Guid pageId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CheckPageVisualContinuityCommand(pageId, GetCurrentUserId()), cancellationToken);
        if (result.IsSuccess) return Ok(result.Value);
        return result.ErrorCode switch
        {
            ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage, code = result.ErrorCode }),
            ResultErrorCodes.AiUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = result.ErrorMessage, code = result.ErrorCode }),
            _ => BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode })
        };
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/pages/{pageId}/content-ready
    /// V2.5 & Module C: Đánh dấu trang kịch bản đã sẵn sàng cho Artist vẽ song song.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/pages/{pageId:guid}/content-ready")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(PageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkContentReady(
        [FromRoute] Guid chapterId,
        [FromRoute] Guid pageId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new MarkPageContentReadyCommand(chapterId, pageId, GetCurrentUserId()), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HistoryError(result);
    }

    /// <summary>
    /// DELETE /api/chapters/{chapterId}/pages/{pageId}/content-ready
    /// V2.5 & Module C: Hủy cờ Content-Ready của trang.
    /// </summary>
    [HttpDelete("api/chapters/{chapterId:guid}/pages/{pageId:guid}/content-ready")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(PageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnmarkContentReady(
        [FromRoute] Guid chapterId,
        [FromRoute] Guid pageId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UnmarkPageContentReadyCommand(chapterId, pageId, GetCurrentUserId()), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HistoryError(result);
    }

    private IActionResult HistoryError<T>(Result<T> result) => this.ConflictOrNull(result) ?? (result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
        _ => BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode })
    });
}

/// <summary>Body khôi phục Page: version stream client đang thấy (BR-11).</summary>
public sealed record RestorePageVersionRequest(long? ExpectedPageVersion);

/// <summary>Form tách panel. RightToLeft = true cho manga đọc phải→trái.</summary>
public sealed class SplitArtworkForm
{
    public IFormFile? File { get; set; }
    public bool RightToLeft { get; set; }
}

// ── Request bodies ───────────────────────────────────────────────────────────

public sealed record CreatePageRequestBody(
    int? PageNumber = null,
    string? LayoutFormat = "StandardPage",
    int WidthPx = 1200,
    int HeightPx = 1800,
    int Dpi = 300
);

public sealed record UpdateCanvasRequestBody(
    int WidthPx,
    int HeightPx,
    int Dpi,
    string LayoutFormat
);
