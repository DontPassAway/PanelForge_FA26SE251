using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.CreateBibleEntry;
using PanelForge.Application.Features.Bible.Commands.LockBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.UploadBibleReferenceImage;
using PanelForge.Application.Features.Bible.Queries.GetBibleReferenceImage;
using PanelForge.Application.Features.Bible.Queries.GetPanelBible;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntriesAtChapter;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntryHistory;
using PanelForge.Application.Features.Bible.Queries.GetSeriesBible;
using PanelForge.Application.Features.Bible.Commands.ReviewBibleEntryRevision;
using PanelForge.Application.Features.Bible.Queries.GetBibleDesignQueue;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

[ApiController]
[Authorize]
public sealed class BibleController : ControllerBase
{
    private readonly IMediator _mediator;

    public BibleController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// GET /api/series/{seriesId}/bible
    /// Lấy toàn bộ danh mục Series Bible kèm revision mới nhất của từng mục (UC-04).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/bible")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(SeriesBibleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeriesBible(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var query = new GetSeriesBibleQuery(seriesId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/series/{seriesId}/bible/at-chapter/{chapterNumber}
    /// Lấy snapshot Series Bible có hiệu lực tại một Chapter cụ thể (BR-15).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/bible/at-chapter/{chapterNumber:int}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<BibleEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBibleEntriesAtChapter(
        [FromRoute] Guid seriesId,
        [FromRoute] int chapterNumber,
        CancellationToken cancellationToken,
        [FromQuery] bool canonOnly = false)
    {
        var query = new GetBibleEntriesAtChapterQuery(seriesId, chapterNumber, canonOnly);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries
    /// Thêm mục mới vào Series Bible (Tự động khởi tạo Version 1 - BR-18, CF1 Step 8).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist)] // Hướng B: Writer/Artist tạo bản nháp, chỉ Producer khoá thành canon
    [ProducesResponseType(typeof(BibleEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateBibleEntry(
        [FromRoute] Guid seriesId,
        [FromBody] CreateBibleEntryRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateBibleEntryCommand(
            SeriesId: seriesId,
            UserId: GetCurrentUserId(),
            Category: body.Category,
            Code: body.Code,
            Name: body.Name,
            Description: body.Description,
            Subtitle: body.Subtitle,
            DetailsJson: body.DetailsJson,
            ReferenceImageUrl: body.EffectiveImageUrl,
            Priority: body.Priority,
            StrictCheck: body.StrictCheck,
            EffectiveFromChapterNumber: body.EffectiveFromChapterNumber,
            StageName: body.StageName,
            EffectiveToChapterNumber: body.EffectiveToChapterNumber
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(nameof(GetSeriesBible), new { seriesId }, result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions
    /// Tạo phiên bản sửa đổi mới cho BibleEntry có hiệu lực từ một Chapter (Append-only BR-01, BR-15).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist)] // bản nháp (chưa khoá)
    [ProducesResponseType(typeof(BibleEntryRevisionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddBibleEntryRevision(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromBody] AddBibleEntryRevisionRequest body,
        CancellationToken cancellationToken)
    {
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: seriesId,
            BibleEntryId: entryId,
            UserId: GetCurrentUserId(),
            Summary: body.EffectiveSummary,
            Name: body.Name,
            Description: body.Description,
            Subtitle: body.Subtitle,
            DetailsJson: body.DetailsJson,
            ReferenceImageUrl: body.EffectiveImageUrl,
            Priority: body.Priority,
            StrictCheck: body.StrictCheck,
            EffectiveFromChapterNumber: body.EffectiveFromChapterNumber,
            StageName: body.StageName,
            EffectiveToChapterNumber: body.EffectiveToChapterNumber,
            ExpectedVersionNumber: body.ExpectedVersionNumber
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/lock
    /// Producer duyệt và khoá một revision của character sheet làm tham chiếu (V1.2). Không mở khoá được:
    /// muốn thay thì thêm revision mới có hiệu lực từ chương khác, hoặc thay thế tại chương đó qua /supersede.
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}/lock")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(BibleEntryRevisionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LockBibleEntryRevision(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromRoute] Guid revisionId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new LockBibleEntryRevisionCommand(seriesId, entryId, revisionId, GetCurrentUserId()), cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/series/{seriesId}/bible/design-queue
    /// V2.5: Lấy hàng đợi việc thiết kế Bible cho Artist (các mục ở trạng thái Draft hoặc ReworkRequired).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/bible/design-queue")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<BibleDesignQueueItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBibleDesignQueue(
        [FromRoute] Guid seriesId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBibleDesignQueueQuery(seriesId), cancellationToken);
        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/endorse
    /// V2.5: Writer hoặc Editor xác nhận duyệt thiết kế Model Sheet của Artist đã chuẩn với cốt truyện (-> WriterEndorsed).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}/endorse")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(BibleEntryRevisionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EndorseBibleEntryRevision(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromRoute] Guid revisionId,
        [FromBody] EndorseBibleRevisionRequest? body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new EndorseBibleEntryRevisionCommand(seriesId, entryId, revisionId, GetCurrentUserId(), body?.EffectiveNote),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/request-rework
    /// V2.5: Writer hoặc Editor yêu cầu Artist vẽ lại kèm nhận xét/feedback (-> ReworkRequired).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}/request-rework")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(BibleEntryRevisionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestReworkBibleEntryRevision(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromRoute] Guid revisionId,
        [FromBody] RequestReworkBibleRevisionRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RequestReworkBibleEntryRevisionCommand(seriesId, entryId, revisionId, GetCurrentUserId(), body.EffectiveReason),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/concept
    /// V2.5: Artist tải lên bản vẽ thiết kế / Model Sheet (CAS SHA-256) cho mục Bible (-> ConceptSubmitted, lưu ArtistUserId).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/concept")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(BibleEntryRevisionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitConceptArt(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromBody] SubmitConceptArtRequest body,
        CancellationToken cancellationToken)
    {
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: seriesId,
            BibleEntryId: entryId,
            UserId: GetCurrentUserId(),
            Summary: body.EffectiveSummary,
            Name: body.Name,
            Description: body.Description,
            Subtitle: body.Subtitle,
            DetailsJson: body.DetailsJson,
            ReferenceImageUrl: body.EffectiveImageUrl,
            StageName: body.StageName,
            EffectiveToChapterNumber: body.EffectiveToChapterNumber,
            ExpectedVersionNumber: body.ExpectedVersionNumber,
            ArtistUserId: GetCurrentUserId(),
            ReviewStatus: BibleRevisionStatus.ConceptSubmitted
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/supersede
    /// Producer thay thế một bản character sheet đã khoá (V1.2): tạo revision mới cùng chương, khoá luôn, ghi lý do.
    /// Bản cũ giữ nguyên trong lịch sử, không mở khoá (BR-01).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}/supersede")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(BibleEntryRevisionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SupersedeBibleEntryRevision(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromRoute] Guid revisionId,
        [FromBody] SupersedeBibleEntryRevisionRequest body,
        CancellationToken cancellationToken)
    {
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: seriesId,
            BibleEntryId: entryId,
            UserId: GetCurrentUserId(),
            Summary: body.Reason,
            Name: body.Name,
            Description: body.Description,
            Subtitle: body.Subtitle,
            DetailsJson: body.DetailsJson,
            ReferenceImageUrl: body.EffectiveImageUrl,
            Priority: body.Priority,
            StrictCheck: body.StrictCheck,
            ExpectedVersionNumber: body.ExpectedVersionNumber,
            SupersedesRevisionId: revisionId,
            SupersedeReason: body.Reason,
            StageName: body.StageName,
            EffectiveToChapterNumber: body.EffectiveToChapterNumber
        );
        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/assets (multipart/form-data: file) — Producer tải ảnh reference sheet (PNG/JPEG/WebP, ≤ 10 MB).
    /// Trả về <c>url</c> để gửi vào <c>referenceImageUrl</c> khi tạo mục hoặc thêm revision.
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/assets")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadBibleReferenceImageCommandHandler.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadBibleReferenceImageCommandHandler.MaxBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(BibleReferenceImageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadReferenceImage(
        [FromRoute] Guid seriesId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Thiếu file ảnh." });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        var result = await _mediator.Send(new UploadBibleReferenceImageCommand(seriesId, buffer.ToArray()), cancellationToken);
        return result.IsSuccess
            ? Created(result.Value!.Url, result.Value)
            : BadRequest(new { error = result.ErrorMessage });
    }

    /// <summary>
    /// GET /api/series/{seriesId}/bible/assets/{sha256} — ảnh reference sheet, mọi thành viên Workspace (FR-Artist-04).
    /// Cần Bearer: FE tải bằng fetch rồi hiển thị qua blob URL.
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/bible/assets/{sha256}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReferenceImage(
        [FromRoute] Guid seriesId, [FromRoute] string sha256, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBibleReferenceImageQuery(seriesId, sha256), cancellationToken);
        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        // Nội dung gắn theo hash nên không đổi; vẫn là tài sản nội bộ nên chỉ cache ở trình duyệt
        Response.Headers.CacheControl = "private, max-age=86400, immutable";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    /// <summary>
    /// GET /api/panels/{panelId}/bible — mục Bible của các nhân vật nói trong Panel, bản canon đã khoá
    /// có hiệu lực ở chương của Panel (UC-07 bước 1, BR-15). Dùng cho drawer tham chiếu trong task của Artist.
    /// </summary>
    [HttpGet("api/panels/{panelId:guid}/bible")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<BibleEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPanelBible([FromRoute] Guid panelId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPanelBibleQuery(panelId), cancellationToken);
        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/series/{seriesId}/bible/entries/{entryId}/history
    /// Lấy toàn bộ lịch sử các phiên bản của một mục trong Bible (BR-18).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/history")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<BibleEntryRevisionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBibleEntryHistory(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        CancellationToken cancellationToken)
    {
        var query = new GetBibleEntryHistoryQuery(seriesId, entryId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }
}
