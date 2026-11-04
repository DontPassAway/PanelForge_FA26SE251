using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Models;
using PanelForge.Application.Features.Chapters.Commands.ApproveChapter;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Queries.GetChapterLockGate;
using PanelForge.Application.Features.Chapters.Commands.CreateChapter;
using PanelForge.Application.Features.Chapters.Commands.DeleteChapter;
using PanelForge.Application.Features.Chapters.Commands.PublishChapter;
using PanelForge.Application.Features.Chapters.Commands.ReopenChapter;
using PanelForge.Application.Features.Chapters.Commands.SetChapterCover;
using PanelForge.Application.Features.Chapters.Commands.SetChapterOutline;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCover;
using PanelForge.Application.Features.Chapters.Commands.UpdateChapter;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Queries.GetChapterById;
using PanelForge.Application.Features.Chapters.Queries.GetChapterReviews;
using PanelForge.Application.Features.Chapters.Queries.GetChapterSnapshot;
using PanelForge.Application.Features.Chapters.Queries.GetChapterSnapshots;
using PanelForge.Application.Features.Chapters.Queries.GetChaptersBySeries;
using PanelForge.Application.Features.Consistency.Commands;
using PanelForge.Application.Features.Consistency.Models;
using PanelForge.Application.Features.Consistency.Queries;
using PanelForge.Application.Features.Chapters.Queries.CheckChapterBibleGate;
using PanelForge.Application.Features.Chapters.Commands.FreezeScript;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

[ApiController]
[Authorize]
public sealed class ChaptersController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChaptersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// POST /api/series/{seriesId}/chapters
    /// Tạo một tập/chương mới cho Series (UC-03, Release Calendar).
    /// </summary>
    [HttpPost("api/series/{seriesId:guid}/chapters")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChapterDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateChapter(
        [FromRoute] Guid seriesId,
        [FromBody] CreateChapterRequest body,
        CancellationToken cancellationToken)
    {
        var command = new CreateChapterCommand(
            SeriesId: seriesId,
            UserId: GetCurrentUserId(),
            ChapterNumber: body.ChapterNumber,
            Title: body.Title,
            TargetReleaseDate: body.TargetReleaseDate,
            Outline: body.Outline
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage?.Contains("không tồn tại") == true)
                return NotFound(new { error = result.ErrorMessage });

            return this.ConflictOrNull(result) ?? BadRequest(new { error = result.ErrorMessage });
        }

        return CreatedAtAction(nameof(GetChapterById), new { chapterId = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// GET /api/series/{seriesId}/chapters?pageIndex=1&pageSize=20
    /// Lấy danh sách chương của Series có hỗ trợ phân trang (Release Calendar).
    /// </summary>
    [HttpGet("api/series/{seriesId:guid}/chapters")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(PagedResult<ChapterDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChaptersBySeries(
        [FromRoute] Guid seriesId,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new GetChaptersBySeriesQuery(seriesId, pageIndex, pageSize);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/chapters/{chapterId}
    /// Lấy thông tin chi tiết của một chương kèm danh sách Scene và Page.
    /// </summary>
    [HttpGet("api/chapters/{chapterId:guid}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(ChapterDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChapterById(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken)
    {
        var query = new GetChapterByIdQuery(chapterId);
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/chapters/{chapterId}/cover (multipart/form-data: file) — Producer đặt ảnh bìa chương (PNG/JPEG/WebP, ≤ 5 MB).
    /// Trả <c>value</c> = coverUrl mới (cũng có trong ChapterDto.coverUrl).
    /// </summary>
    [HttpPut("api/chapters/{chapterId:guid}/cover")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SetChapterCoverCommandHandler.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = SetChapterCoverCommandHandler.MaxBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(TextValueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetCover([FromRoute] Guid chapterId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Thiếu file ảnh bìa." });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return CoverResult(await _mediator.Send(new SetChapterCoverCommand(chapterId, GetCurrentUserId(), buffer.ToArray()), cancellationToken));
    }

    /// <summary>DELETE /api/chapters/{chapterId}/cover — Producer xoá ảnh bìa chương (FE quay về badge số tập).</summary>
    [HttpDelete("api/chapters/{chapterId:guid}/cover")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(TextValueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearCover([FromRoute] Guid chapterId, CancellationToken cancellationToken)
        => CoverResult(await _mediator.Send(new SetChapterCoverCommand(chapterId, GetCurrentUserId(), null), cancellationToken));

    /// <summary>
    /// GET /api/chapters/{chapterId}/cover — ảnh bìa hiện tại, mọi thành viên Workspace. Cần Bearer: FE tải bằng fetch rồi
    /// hiển thị qua blob URL. Dùng đúng <c>coverUrl</c> trong DTO (có <c>?v=</c> theo nội dung) để không dính cache bìa cũ.
    /// </summary>
    [HttpGet("api/chapters/{chapterId:guid}/cover")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCover([FromRoute] Guid chapterId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetChapterCoverQuery(chapterId), cancellationToken);
        if (!result.IsSuccess)
            return NotFound(new { error = result.ErrorMessage });

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    private IActionResult CoverResult(Result<string?> result)
        => result.IsSuccess
            ? Ok(new TextValueResponse(result.Value))
            : result.ErrorCode == ResultErrorCodes.NotFound
                ? NotFound(new { error = result.ErrorMessage })
                : BadRequest(new { error = result.ErrorMessage });

    /// <summary>
    /// PUT /api/chapters/{chapterId}
    /// Cập nhật tên, ngày phát hành mục tiêu hoặc duyệt tăng Version của chương (Release Calendar).
    /// </summary>
    [HttpPut("api/chapters/{chapterId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(ChapterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateChapter(
        [FromRoute] Guid chapterId,
        [FromBody] UpdateChapterRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateChapterCommand(
            ChapterId: chapterId,
            UserId: GetCurrentUserId(),
            Title: body.Title,
            TargetReleaseDate: body.TargetReleaseDate,
            IncrementVersion: body.IncrementVersion,
            Outline: body.Outline
        );

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
    /// DELETE /api/chapters/{chapterId}
    /// Xóa mềm một chương (Soft-delete).
    /// </summary>
    [HttpDelete("api/chapters/{chapterId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteChapter(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken)
    {
        var command = new DeleteChapterCommand(chapterId, GetCurrentUserId());
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return this.ConflictOrNull(result) ?? NotFound(new { error = result.ErrorMessage });

        return Ok(new { message = $"Chương {chapterId} đã được xóa thành công." });
    }

    /// <summary>
    /// GET /api/chapters/{chapterId}/bible-gate
    /// V2.5: Cổng kiểm tra Series Bible theo Chương (Chapter-Scoped Bible Gate).
    /// Quét toàn bộ nhân vật và bối cảnh được dùng trong kịch bản chương, xác thực có bản CanonLocked tại chương hay chưa.
    /// </summary>
    [HttpGet("api/chapters/{chapterId:guid}/bible-gate")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(ChapterBibleGateResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckChapterBibleGate(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new CheckChapterBibleGateQuery(chapterId), cancellationToken));

    /// <summary>
    /// POST /api/chapters/{chapterId}/freeze-script
    /// V2.5: Producer duyệt kịch bản và bắt đầu sản xuất (Freeze Script & Hand-off Gate).
    /// Đóng băng kịch bản thành read-only cho Writer và mở khóa phân công layout cho Artist.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/freeze-script")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChapterDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> FreezeChapterScript(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new FreezeChapterScriptCommand(chapterId, GetCurrentUserId()), cancellationToken));

    /// <summary>
    /// POST /api/chapters/{chapterId}/unfreeze-script
    /// V2.5: Producer mở lại kịch bản cho Writer sửa đổi.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/unfreeze-script")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChapterDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnfreezeChapterScript(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new UnfreezeChapterScriptCommand(chapterId, GetCurrentUserId()), cancellationToken));

    /// <summary>
    /// GET /api/chapters/{chapterId}/lock-gate — Producer xem MỌI điều kiện chưa đạt trước khi duyệt chương (tất định, không LLM).
    /// Item blocking = POST /approve sẽ trả 409 với đúng message đó.
    /// </summary>
    [HttpGet("api/chapters/{chapterId:guid}/lock-gate")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChapterLockGateDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChapterLockGate([FromRoute] Guid chapterId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetChapterLockGateQuery(chapterId), cancellationToken));

    /// <summary>
    /// POST /api/chapters/{chapterId}/approve
    /// Producer duyệt bản hoàn chỉnh (InProduction → Approved) và chuyển cho Publisher.
    /// </summary>
    /// <summary>
    /// POST /api/chapters/{chapterId}/request-script-reopen
    /// V2.5 & WF04: Writer gửi yêu cầu mở lại kịch bản có lý do chính đáng tới Producer.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/request-script-reopen")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(ChapterDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestScriptReopen(
        [FromRoute] Guid chapterId,
        [FromBody] RequestScriptReopenRequest body,
        CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new RequestScriptReopenCommand(chapterId, GetCurrentUserId(), body.Reason), cancellationToken));

    /// <summary>
    /// POST /api/chapters/{chapterId}/approve-script-reopen
    /// V2.5 & WF04: Producer duyệt mở lại kịch bản (ScriptFrozen -> ScriptOpen).
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/approve-script-reopen")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChapterDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveScriptReopen(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new ApproveScriptReopenCommand(chapterId, GetCurrentUserId()), cancellationToken));

    /// <summary>
    /// POST /api/chapters/{chapterId}/reject-script-reopen
    /// V2.5 & WF04: Producer từ chối yêu cầu mở lại kịch bản.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/reject-script-reopen")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChapterDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectScriptReopen(
        [FromRoute] Guid chapterId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new RejectScriptReopenCommand(chapterId, GetCurrentUserId()), cancellationToken));

    [HttpPost("api/chapters/{chapterId:guid}/approve")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChapterReviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveChapter([FromRoute] Guid chapterId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new ApproveChapterCommand(chapterId, GetCurrentUserId()), cancellationToken));

    /// <summary>
    /// POST /api/chapters/{chapterId}/reopen
    /// Producer mở lại, hoặc Publisher từ chối xuất bản: Approved → InProduction, bắt buộc lý do.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/reopen")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Publisher)]
    [ProducesResponseType(typeof(ChapterReviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReopenChapter(
        [FromRoute] Guid chapterId, [FromBody] ReopenChapterRequest body, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new ReopenChapterCommand(chapterId, GetCurrentUserId(), body.Comment), cancellationToken));

    /// <summary>
    /// POST /api/chapters/{chapterId}/publish
    /// Publisher duyệt xuất bản và lên lịch (Approved → Published). publishAt bỏ trống = phát hành ngay.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/publish")]
    [RequireWorkspaceRole(WorkspaceRole.Publisher)]
    [ProducesResponseType(typeof(ChapterReviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishChapter(
        [FromRoute] Guid chapterId, [FromBody] PublishChapterRequest? body, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new PublishChapterCommand(chapterId, GetCurrentUserId(), body?.PublishAt), cancellationToken));

    /// <summary>GET /api/chapters/{chapterId}/reviews — lịch sử phê duyệt cấp Chapter (thành viên Workspace).</summary>
    [HttpGet("api/chapters/{chapterId:guid}/reviews")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<ChapterReviewDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChapterReviews([FromRoute] Guid chapterId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetChapterReviewsQuery(chapterId), cancellationToken));

    /// <summary>GET /api/chapters/{chapterId}/snapshots — CF4 / 4.1: các bản chụp bất biến lúc Producer duyệt, mới nhất trước (thành viên Workspace).</summary>
    [HttpGet("api/chapters/{chapterId:guid}/snapshots")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<ChapterSnapshotDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChapterSnapshots([FromRoute] Guid chapterId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetChapterSnapshotsQuery(chapterId), cancellationToken));

    /// <summary>GET /api/chapters/{chapterId}/snapshots/{snapshotId} — manifest của một snapshot kèm kết quả kiểm hash.</summary>
    [HttpGet("api/chapters/{chapterId:guid}/snapshots/{snapshotId:guid}")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(ChapterSnapshotDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChapterSnapshot(
        [FromRoute] Guid chapterId, [FromRoute] Guid snapshotId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetChapterSnapshotQuery(chapterId, snapshotId), cancellationToken));

    /// <summary>PUT /api/chapters/{chapterId}/outline — Producer/Writer ghi outline chương (rỗng = xoá). Trả outline đã lưu.</summary>
    [HttpPut("api/chapters/{chapterId:guid}/outline")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(TextValueResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetOutline(
        [FromRoute] Guid chapterId, [FromBody] SetChapterOutlineRequest body, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetChapterOutlineCommand(chapterId, GetCurrentUserId(), body.Outline), cancellationToken);
        return result.IsSuccess ? Ok(new TextValueResponse(result.Value)) : ToActionResult(result);
    }

    /// <summary>
    /// POST /api/chapters/{chapterId}/consistency-check — CF3: chạy kiểm tra nhất quán bằng code trên script của chương
    /// (so với Series Bible hiệu lực ở chương + ConsistencyRule đang bật). Producer, Editor, Writer.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/consistency-check")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor, WorkspaceRole.Writer)]
    [ProducesResponseType(typeof(ConsistencyCheckResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RunConsistencyCheck([FromRoute] Guid chapterId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new RunConsistencyCheckCommand(chapterId, GetCurrentUserId()), cancellationToken));

    /// <summary>GET /api/chapters/{chapterId}/consistency-findings?status= — finding của chương (thành viên Workspace).</summary>
    [HttpGet("api/chapters/{chapterId:guid}/consistency-findings")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<ConsistencyFindingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConsistencyFindings(
        [FromRoute] Guid chapterId, [FromQuery] ConsistencyFindingStatus? status, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetConsistencyFindingsQuery(chapterId, status), cancellationToken));

    /// <summary>
    /// POST /api/chapters/{chapterId}/consistency-findings/{findingId}/triage — CF3 bước 5 (BR-21): Editor/Producer phân loại
    /// finding đang mở. Body { decision: Confirmed|Rejected|Ignored|NeedsClarification, note }.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/consistency-findings/{findingId:guid}/triage")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(ConsistencyFindingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> TriageConsistencyFinding(
        [FromRoute] Guid chapterId, [FromRoute] Guid findingId, [FromBody] TriageConsistencyFindingRequest body,
        CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(
            new TriageConsistencyFindingCommand(chapterId, findingId, GetCurrentUserId(), body.Decision, body.Note), cancellationToken));

    /// <summary>
    /// GET /api/chapters/{chapterId}/visual-consistency-report — Báo cáo nhất quán hình ảnh cho Editor (CF3, V1.2).
    /// </summary>
    [HttpGet("api/chapters/{chapterId:guid}/visual-consistency-report")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor, WorkspaceRole.Writer, WorkspaceRole.Artist)]
    [ProducesResponseType(typeof(VisualConsistencyReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVisualConsistencyReport(
        [FromRoute] Guid chapterId, [FromQuery] decimal? threshold, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetVisualConsistencyReportQuery(chapterId, threshold), cancellationToken));

    /// <summary>
    /// POST /api/chapters/{chapterId}/visual-consistency-check — Chạy kiểm tra nhất quán hình ảnh so với Character Sheet khóa.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/visual-consistency-check")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(VisualConsistencyReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RunVisualConsistencyCheck(
        [FromRoute] Guid chapterId, [FromBody] RunVisualConsistencyCheckRequest? body, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new RunVisualConsistencyCheckCommand(chapterId, GetCurrentUserId(), body?.Threshold), cancellationToken));

    private IActionResult ToActionResult<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
        ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
        _ => BadRequest(new { error = result.ErrorMessage })
    };
}

public sealed record RequestScriptReopenRequest(string Reason);

public sealed record SetChapterOutlineRequest(string? Outline);

/// <summary>Giá trị text vừa lưu (null = đã xoá).</summary>
public sealed record TextValueResponse(string? Value);
