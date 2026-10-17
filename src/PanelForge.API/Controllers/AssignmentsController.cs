using PanelForge.Application.Features.Assignments.Commands.ReturnWorkFromReview;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Assignments.Commands.ApproveAssignment;
using PanelForge.Application.Features.Assignments.Commands.CreateAssignment;
using PanelForge.Application.Features.Assignments.Commands.DeleteAssignment;
using PanelForge.Application.Features.Assignments.Commands.OpenAssignmentReview;
using PanelForge.Application.Features.Assignments.Commands.RejectChangeRequest;
using PanelForge.Application.Features.Assignments.Commands.ReopenChangeRequest;
using PanelForge.Application.Features.Assignments.Commands.RequestAssignmentChanges;
using PanelForge.Application.Features.Assignments.Commands.ResolveChangeRequest;
using PanelForge.Application.Features.Assignments.Commands.StartAssignment;
using PanelForge.Application.Features.Assignments.Commands.SubmitAssignment;
using PanelForge.Application.Features.Assignments.Commands.UpdateAssignment;
using PanelForge.Application.Features.Assignments.Commands.VerifyChangeRequest;
using PanelForge.Application.Features.Assignments.Queries.GetAssignmentSubmissions;
using PanelForge.Application.Features.Assignments.Queries.GetChangeRequestsByChapter;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Application.Features.Assignments.Queries.GetAssignmentsByChapter;
using PanelForge.Application.Features.Assignments.Queries.GetMyWorkspaceAssignments;
using PanelForge.Application.Features.Progress.Queries.GetSeriesProgress;
using PanelForge.Application.Features.Progress.Queries.GetStageProgressByChapter;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

[ApiController]
[Authorize]
public sealed class AssignmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AssignmentsController(IMediator mediator) => _mediator = mediator;

    private Guid GetCurrentUserId() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    /// <summary>POST /api/chapters/{chapterId}/assignments — Producer giao việc cho Chapter, Page hoặc Panel (UC-05).</summary>
    [HttpPost("api/chapters/{chapterId:guid}/assignments")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(AssignmentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        Guid chapterId, [FromBody] CreateAssignmentRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateAssignmentCommand(
            chapterId, GetCurrentUserId(), body.EntityType, body.TargetEntityId, body.PipelineStageId,
            body.AssigneeUserId, body.Brief, body.ReferenceNotes, body.DueDate, body.Priority,
            body.DependsOnAssignmentId, body.DependsOnAssignmentIds), ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetByChapter), new { chapterId }, result.Value)
            : Error(result);
    }

    /// <summary>GET /api/chapters/{chapterId}/assignments?assigneeUserId=&amp;status= — mọi thành viên Workspace.</summary>
    [HttpGet("api/chapters/{chapterId:guid}/assignments")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<AssignmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByChapter(
        Guid chapterId, [FromQuery] Guid? assigneeUserId, [FromQuery] AssignmentStatus? status, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAssignmentsByChapterQuery(chapterId, assigneeUserId, status), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>GET /api/workspaces/{workspaceId}/my-assignments — danh sách việc cá nhân toàn bộ Studio (Cross-Chapter Worklist).</summary>
    [HttpGet("api/workspaces/{workspaceId:guid}/my-assignments")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<WorkspaceAssignmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWorkspaceAssignments(
        Guid workspaceId, [FromQuery] AssignmentStatus? status, [FromQuery] Guid? seriesId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyWorkspaceAssignmentsQuery(
            workspaceId, GetCurrentUserId(), status, seriesId), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>PUT /api/chapters/{chapterId}/assignments/{assignmentId} — Producer sửa, giao lại, đổi dependency.</summary>
    [HttpPut("api/chapters/{chapterId:guid}/assignments/{assignmentId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(AssignmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid chapterId, Guid assignmentId, [FromBody] UpdateAssignmentRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateAssignmentCommand(
            chapterId, assignmentId, GetCurrentUserId(), body.Brief, body.ReferenceNotes, body.DueDate,
            body.Priority, body.AssigneeUserId, body.DependsOnAssignmentId, body.DependsOnAssignmentIds), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>DELETE /api/chapters/{chapterId}/assignments/{assignmentId} — Producer xoá mềm hoặc huỷ Assignment (UC-05).</summary>
    [HttpDelete("api/chapters/{chapterId:guid}/assignments/{assignmentId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid chapterId, Guid assignmentId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteAssignmentCommand(chapterId, assignmentId, GetCurrentUserId()), ct);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    /// <summary>POST .../assignments/{assignmentId}/start — chỉ người được giao.</summary>
    [HttpPost("api/chapters/{chapterId:guid}/assignments/{assignmentId:guid}/start")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(AssignmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Start(Guid chapterId, Guid assignmentId, CancellationToken ct)
    {
        var result = await _mediator.Send(new StartAssignmentCommand(chapterId, assignmentId, GetCurrentUserId()), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST .../assignments/{assignmentId}/submit — chỉ người được giao.</summary>
    [HttpPost("api/chapters/{chapterId:guid}/assignments/{assignmentId:guid}/submit")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(AssignmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(Guid chapterId, Guid assignmentId, CancellationToken ct)
    {
        var result = await _mediator.Send(new SubmitAssignmentCommand(chapterId, assignmentId, GetCurrentUserId()), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST .../assignments/{assignmentId}/open-review — Editor/Producer mở việc đã nộp (CF3). Không tự duyệt việc của mình.</summary>
    [HttpPost("api/chapters/{chapterId:guid}/assignments/{assignmentId:guid}/open-review")]
    [RequireWorkspaceRole(WorkspaceRole.Editor, WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(AssignmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> OpenReview(Guid chapterId, Guid assignmentId, CancellationToken ct)
    {
        var result = await _mediator.Send(new OpenAssignmentReviewCommand(chapterId, assignmentId, GetCurrentUserId()), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST .../assignments/{assignmentId}/approve — duyệt cổng, mở stage kế tiếp (CF3 bước 6, BR-06, BR-19).</summary>
    [HttpPost("api/chapters/{chapterId:guid}/assignments/{assignmentId:guid}/approve")]
    [RequireWorkspaceRole(WorkspaceRole.Editor, WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(AssignmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(
        Guid chapterId, Guid assignmentId, [FromBody] ApproveAssignmentRequest? body, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ApproveAssignmentCommand(chapterId, assignmentId, GetCurrentUserId(), body?.ExpectedSubmissionId), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST .../assignments/{assignmentId}/request-changes — trả về, comment bắt buộc (CF3 bước 7, BR-06).</summary>
    [HttpPost("api/chapters/{chapterId:guid}/assignments/{assignmentId:guid}/request-changes")]
    [RequireWorkspaceRole(WorkspaceRole.Editor, WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(AssignmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestChanges(
        Guid chapterId, Guid assignmentId, [FromBody] RequestAssignmentChangesRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new RequestAssignmentChangesCommand(
                chapterId, assignmentId, GetCurrentUserId(), body.Comment, body.Changes, body.ExpectedSubmissionId), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>
    /// POST .../assignments/{reviewAssignmentId}/return-work — từ màn duyệt cả chương (vd 7. Review), trả việc của một trang/khung
    /// về người làm kèm mô tả + vùng khoanh. Body: { targetAssignmentId, description, regionJson?, panelId?, assetReferenceId?, dueDate? }.
    /// </summary>
    [HttpPost("api/chapters/{chapterId:guid}/assignments/{reviewAssignmentId:guid}/return-work")]
    [RequireWorkspaceRole(WorkspaceRole.Editor, WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChangeRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReturnWorkFromReview(
        Guid chapterId, Guid reviewAssignmentId, [FromBody] ReturnWorkFromReviewRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new ReturnWorkFromReviewCommand(
            chapterId, reviewAssignmentId, body.TargetAssignmentId, GetCurrentUserId(), body.Description ?? "",
            body.RegionJson, body.PanelId, body.AssetReferenceId, body.DueDate), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>GET .../assignments/{assignmentId}/submissions — lịch sử các lần nộp (mọi thành viên Workspace).</summary>
    [HttpGet("api/chapters/{chapterId:guid}/assignments/{assignmentId:guid}/submissions")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<SubmissionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubmissions(Guid chapterId, Guid assignmentId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAssignmentSubmissionsQuery(chapterId, assignmentId), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>GET /api/chapters/{chapterId}/change-requests?assignmentId=&amp;status=&amp;responsibleUserId= (mọi thành viên Workspace).</summary>
    [HttpGet("api/chapters/{chapterId:guid}/change-requests")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<ChangeRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChangeRequests(
        Guid chapterId, [FromQuery] Guid? assignmentId, [FromQuery] ChangeRequestStatus? status,
        [FromQuery] Guid? responsibleUserId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetChangeRequestsByChapterQuery(chapterId, assignmentId, status, responsibleUserId), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST .../change-requests/{changeRequestId}/resolve — chỉ người chịu trách nhiệm báo đã sửa (Open → Resolved).</summary>
    [HttpPost("api/chapters/{chapterId:guid}/change-requests/{changeRequestId:guid}/resolve")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(ChangeRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveChangeRequest(
        Guid chapterId, Guid changeRequestId, [FromBody] ResolveChangeRequestRequest? body, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ResolveChangeRequestCommand(chapterId, changeRequestId, GetCurrentUserId(), body?.Note), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST .../change-requests/{changeRequestId}/verify — Editor/Producer xác minh đã đạt (Resolved → Verified), body tuỳ chọn { comment }.</summary>
    [HttpPost("api/chapters/{chapterId:guid}/change-requests/{changeRequestId:guid}/verify")]
    [RequireWorkspaceRole(WorkspaceRole.Editor, WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChangeRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyChangeRequest(
        Guid chapterId, Guid changeRequestId, [FromBody] ChangeRequestCommentRequest? body, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new VerifyChangeRequestCommand(chapterId, changeRequestId, GetCurrentUserId(), body?.Comment), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST .../change-requests/{changeRequestId}/reopen — Editor/Producer mở lại vì chưa đạt (Resolved → Open), comment bắt buộc.</summary>
    [HttpPost("api/chapters/{chapterId:guid}/change-requests/{changeRequestId:guid}/reopen")]
    [RequireWorkspaceRole(WorkspaceRole.Editor, WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChangeRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReopenChangeRequest(
        Guid chapterId, Guid changeRequestId, [FromBody] ChangeRequestCommentRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ReopenChangeRequestCommand(chapterId, changeRequestId, GetCurrentUserId(), body.Comment), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>POST .../change-requests/{changeRequestId}/reject — Editor/Producer chấp nhận phản đối, đóng yêu cầu (Open → Rejected), lý do bắt buộc.</summary>
    [HttpPost("api/chapters/{chapterId:guid}/change-requests/{changeRequestId:guid}/reject")]
    [RequireWorkspaceRole(WorkspaceRole.Editor, WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ChangeRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectChangeRequest(
        Guid chapterId, Guid changeRequestId, [FromBody] ChangeRequestCommentRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new RejectChangeRequestCommand(chapterId, changeRequestId, GetCurrentUserId(), body.Comment), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>GET /api/chapters/{chapterId}/stage-progress?pageId= — tiến độ theo Panel × Stage của Chapter (mọi thành viên).</summary>
    [HttpGet("api/chapters/{chapterId:guid}/stage-progress")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(IReadOnlyList<StageProgressDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStageProgress(Guid chapterId, [FromQuery] Guid? pageId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetStageProgressByChapterQuery(chapterId, pageId), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    /// <summary>GET /api/series/{seriesId}/progress — tổng hợp tiến độ Series cho Producer (UC-16): theo stage/panel, khối lượng việc, quá hạn, burndown.</summary>
    [HttpGet("api/series/{seriesId:guid}/progress")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(SeriesProgressDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSeriesProgress(Guid seriesId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSeriesProgressQuery(seriesId), ct);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    private IActionResult Error<T>(Result<T> result) => result.ErrorCode switch
    {
        ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
        ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
        ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage, code = result.ErrorCode }),
        _ => BadRequest(new { error = result.ErrorMessage })
    };
}

/// <summary>Body trả việc từ màn duyệt cả chương.</summary>
public sealed record ReturnWorkFromReviewRequest(
    Guid TargetAssignmentId, string? Description, string? RegionJson = null, Guid? PanelId = null,
    Guid? AssetReferenceId = null, DateTime? DueDate = null);
