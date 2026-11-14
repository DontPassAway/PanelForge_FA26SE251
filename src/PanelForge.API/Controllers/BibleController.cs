using System.Security.Claims;
using MediatR;
using PanelForge.Application.Features.Bible.Commands.SubmitBibleConcept;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.CancelReplacementRevision;
using PanelForge.Application.Features.Bible.Commands.ConfirmBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.DefineBibleEntry;
using PanelForge.Application.Features.Bible.Commands.SetBibleEntryCrowd;
using PanelForge.Application.Features.Bible.Commands.SetChapterCastRevision;
using PanelForge.Application.Features.Bible.Commands.ConceptComments;
using PanelForge.Application.Features.Bible.Commands.DeleteBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.CreateBibleEntry;
using PanelForge.Application.Features.Bible.Commands.UploadBibleReferenceImage;
using PanelForge.Application.Features.Bible.Queries.GetBibleConceptComments;
using PanelForge.Application.Features.Bible.Queries.GetBibleReferenceImage;
using PanelForge.Application.Features.Bible.Queries.GetPanelBible;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntriesAtChapter;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntryHistory;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntryUsages;
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
    /// V2.6 (Q19/Q20): Chỉ Writer mới có quyền tạo mục Bible; Producer chỉ đọc Bible.
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries")]
    [RequireWorkspaceRole(WorkspaceRole.Writer)]
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
            StageName: body.StageName,
            IsCrowd: body.IsCrowd
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorCode == ResultErrorCodes.NotFound || result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            if (result.ErrorCode == ResultErrorCodes.Conflict)
                return Conflict(new { error = result.ErrorMessage, code = result.ErrorCode });

            return BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(nameof(GetSeriesBible), new { seriesId }, result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions
    /// Tạo phiên bản sửa đổi mới cho BibleEntry có hiệu lực từ một Chapter (Append-only BR-01, BR-15).
    /// V2.6 (Q19/Q20): Chỉ Writer mới có quyền tạo revision text; Producer chỉ đọc.
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions")]
    [RequireWorkspaceRole(WorkspaceRole.Writer)]
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
            StageName: body.StageName,
            ExpectedVersionNumber: body.ExpectedVersionNumber,
            IsNewVariant: body.IsNewVariant,
            TargetRevisionId: body.TargetRevisionId,
            VariantLabel: body.VariantLabel
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
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/variants
    /// Tạo biến thể tạo hình mới cho BibleEntry (BIB-02, Q16).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/variants")]
    [RequireWorkspaceRole(WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(BibleEntryRevisionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateBibleVariant(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromBody] CreateBibleVariantRequest body,
        CancellationToken cancellationToken)
    {
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: seriesId,
            BibleEntryId: entryId,
            UserId: GetCurrentUserId(),
            Summary: body.Summary,
            Name: body.Name,
            Description: body.Description,
            Subtitle: body.Subtitle,
            DetailsJson: body.DetailsJson,
            ReferenceImageUrl: body.ReferenceImageUrl,
            Priority: body.Priority,
            StrictCheck: body.StrictCheck,
            StageName: body.VariantLabel,
            IsNewVariant: true
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
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/confirm
    /// V2.6 (Q20): Editor xác nhận bản vẽ Concept = khoá Canon (chỉ Editor; Editor khác Artist đã nộp).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}/confirm")]
    [RequireWorkspaceRole(WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(BibleEntryRevisionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmBibleEntryRevision(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromRoute] Guid revisionId,
        [FromBody] ConfirmBibleRevisionRequest? body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ConfirmBibleEntryRevisionCommand(seriesId, entryId, revisionId, GetCurrentUserId(), body?.ConceptEventId),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage }),
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
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/request-rework
    /// Writer/Editor (khi duyệt) hoặc Producer (thay vì khoá) yêu cầu Artist vẽ lại kèm nhận xét (-> ReworkRequired).
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
    /// V2.5: Artist tải lên bản vẽ thiết kế / Model Sheet cho phiên bản hiện hành (-> ConceptSubmitted). Chỉ ảnh = thêm sự kiện,
    /// không tăng version; kèm sửa nội dung = phiên bản mới; phiên bản đã khoá = 409 (dùng /supersede).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/concept")]
    [RequireWorkspaceRole(WorkspaceRole.Artist)]
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
        var result = await _mediator.Send(
            new SubmitBibleConceptCommand(seriesId, entryId, GetCurrentUserId(), body), cancellationToken);
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
    /// Writer thay thế một bản character sheet đã khoá: tạo revision mới cùng chương, ghi lý do.
    /// V2.6 (Q19/Q20): Chỉ Writer mới có quyền supersede.
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}/supersede")]
    [RequireWorkspaceRole(WorkspaceRole.Writer)]
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
            ChapterId: body.ChapterId,
            VariantLabel: body.VariantLabel
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
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/cancel-replacement
    /// Writer huỷ bản thay thế chưa có sự kiện kiểm duyệt (Q23); binding đang trỏ bản đó quay về bản kế thừa.
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}/cancel-replacement")]
    [RequireWorkspaceRole(WorkspaceRole.Writer)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelReplacementRevision(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromRoute] Guid revisionId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CancelReplacementRevisionCommand(seriesId, entryId, revisionId, GetCurrentUserId()),
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

        return Ok(new { success = true });
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/crowd
    /// Writer bật/tắt cờ Quần chúng cho mục Bible (BIB-13, 7A, Q19).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/crowd")]
    [RequireWorkspaceRole(WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(BibleEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetBibleEntryCrowd(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromBody] SetBibleEntryCrowdRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SetBibleEntryCrowdCommand(seriesId, entryId, request.IsCrowd, GetCurrentUserId()),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/define
    /// Writer viết định nghĩa cho mục nháp (IsStub) (BIB-13, 4A, Q19).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/define")]
    [RequireWorkspaceRole(WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(BibleEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DefineBibleEntry(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromBody] DefineBibleEntryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new DefineBibleEntryCommand(
                SeriesId: seriesId,
                BibleEntryId: entryId,
                UserId: GetCurrentUserId(),
                Name: request.Name,
                Description: request.Description,
                Subtitle: request.Subtitle,
                DetailsJson: request.DetailsJson,
                ReferenceImageUrl: request.ReferenceImageUrl,
                Priority: request.Priority,
                StrictCheck: request.StrictCheck,
                Code: request.Code),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage }),
                ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/cast/{entryId}/revision
    /// Writer đổi bản gắn cho chương hoặc trả về bản kế thừa (BIB-13, Q18, Q19, Q23, Q24).
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/cast/{entryId:guid}/revision")]
    [RequireWorkspaceRole(WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(SetChapterCastRevisionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetChapterCastRevision(
        [FromRoute] Guid chapterId,
        [FromRoute] Guid entryId,
        [FromBody] SetChapterCastRevisionRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SetChapterCastRevisionCommand(chapterId, entryId, request?.RevisionId, GetCurrentUserId()),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage }),
                ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/assets (multipart/form-data: file) — Artist tải ảnh reference sheet (PNG/JPEG/WebP, ≤ 10 MB).
    /// Trả về <c>url</c> để gửi vào <c>referenceImageUrl</c> khi tạo mục hoặc thêm revision.
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/assets")]
    [RequireWorkspaceRole(WorkspaceRole.Artist)]
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
    /// <summary>
    /// GET /api/series/{seriesId}/bible/entries/{entryId}/usages
    /// Các chương có kịch bản dùng mục này (người nói hoặc @Tag) và đã đạt điều kiện Canon của Gate 1 chưa (BR-21).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/usages")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<BibleEntryUsageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBibleEntryUsages(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBibleEntryUsagesQuery(seriesId, entryId), cancellationToken);
        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// DELETE /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}
    /// Xóa một bản sửa đổi (revision) của mục Bible (chỉ cho phép nếu chưa khóa Canon và không phải bản gốc ban đầu).
    /// </summary>
    [HttpDelete("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRevision(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromRoute] Guid revisionId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new DeleteBibleEntryRevisionCommand(seriesId, entryId, revisionId, userId), cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorCode == ResultErrorCodes.NotFound) return NotFound(new { error = result.ErrorMessage });
            return BadRequest(new { error = result.ErrorMessage });
        }
        return NoContent();
    }

    /// <summary>
    /// GET /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/comments
    /// Lấy danh sách góp ý và thảo luận của một revision.
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}/comments")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<BibleConceptCommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConceptComments(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromRoute] Guid revisionId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetBibleConceptCommentsQuery(seriesId, entryId, revisionId), cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/comments
    /// Thêm góp ý gốc cho một phiên bản ConceptArt (chỉ Writer và Editor).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}/comments")]
    [RequireWorkspaceRole(WorkspaceRole.Writer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(BibleConceptCommentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateConceptComment(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromRoute] Guid revisionId,
        [FromBody] CreateConceptCommentRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateBibleConceptCommentCommand(seriesId, entryId, revisionId, GetCurrentUserId(), body), cancellationToken);

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
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/comments/{commentId}/replies
    /// Trả lời góp ý (chỉ Writer, Editor, Artist).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}/comments/{commentId:guid}/replies")]
    [RequireWorkspaceRole(WorkspaceRole.Writer, WorkspaceRole.Editor, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(BibleConceptCommentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplyConceptComment(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromRoute] Guid revisionId,
        [FromRoute] Guid commentId,
        [FromBody] ReplyConceptCommentRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ReplyBibleConceptCommentCommand(seriesId, entryId, revisionId, commentId, GetCurrentUserId(), body), cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// POST /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/comments/{commentId}/resolve
    /// Đánh dấu góp ý đã được xử lý (chỉ Writer và Editor).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/bible/entries/{entryId:guid}/revisions/{revisionId:guid}/comments/{commentId:guid}/resolve")]
    [RequireWorkspaceRole(WorkspaceRole.Writer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(BibleConceptCommentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveConceptComment(
        [FromRoute] Guid seriesId,
        [FromRoute] Guid entryId,
        [FromRoute] Guid revisionId,
        [FromRoute] Guid commentId,
        [FromQuery] bool resolve = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ResolveBibleConceptCommentCommand(seriesId, entryId, revisionId, commentId, GetCurrentUserId(), resolve), cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
                _ => BadRequest(new { error = result.ErrorMessage })
            };
        }

        return Ok(result.Value);
    }
}

public sealed record SetBibleEntryCrowdRequest(bool IsCrowd);

public sealed record SetChapterCastRevisionRequest(Guid? RevisionId = null);

public sealed record DefineBibleEntryRequest(
    string Name,
    string Description,
    string? Subtitle = null,
    string? DetailsJson = null,
    string? ReferenceImageUrl = null,
    BiblePriority Priority = BiblePriority.Standard,
    bool StrictCheck = true,
    string? Code = null
);

