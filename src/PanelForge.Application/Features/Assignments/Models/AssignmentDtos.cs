using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Models;

public sealed record AssignmentDto(
    Guid Id,
    Guid SeriesId,
    Guid ChapterId,
    WorkflowEntityType EntityType,
    Guid TargetEntityId,
    Guid PipelineStageId,
    Guid AssigneeUserId,
    WorkspaceRole AssignedRole,
    string Brief,
    string? ReferenceNotes,
    DateTime? DueDate,
    AssignmentPriority Priority,
    AssignmentStatus Status,
    Guid? DependsOnAssignmentId,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? LastFeedbackComment,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public static AssignmentDto From(Assignment a) => new(
        a.Id, a.SeriesId, a.ChapterId, a.EntityType, a.TargetEntityId, a.PipelineStageId,
        a.AssigneeUserId, a.AssignedRole, a.Brief, a.ReferenceNotes, a.DueDate, a.Priority,
        a.Status, a.DependsOnAssignmentId, a.StartedAt, a.CompletedAt, a.LastFeedbackComment,
        a.CreatedAt, a.UpdatedAt);
}

/// <summary>TargetEntityId bỏ trống khi EntityType = Chapter (mặc định là chính Chapter trên route).</summary>
public sealed record CreateAssignmentRequest(
    WorkflowEntityType EntityType,
    Guid? TargetEntityId,
    Guid PipelineStageId,
    Guid AssigneeUserId,
    string Brief,
    string? ReferenceNotes = null,
    DateTime? DueDate = null,
    AssignmentPriority Priority = AssignmentPriority.Medium,
    Guid? DependsOnAssignmentId = null);

/// <summary>Một yêu cầu sửa cụ thể. RegionJson tuỳ chọn: <c>{ "x", "y", "w", "h" }</c> chuẩn hóa 0–1.</summary>
public sealed record ChangeRequestItemRequest(string Description, string? RegionJson = null, DateTime? DueDate = null);

/// <summary>
/// Trả về bắt buộc có comment (BR-06). Changes bỏ trống = tạo một ChangeRequest từ chính comment.
/// </summary>
public sealed record RequestAssignmentChangesRequest(string? Comment, IReadOnlyList<ChangeRequestItemRequest>? Changes = null);

/// <summary>Người chịu trách nhiệm báo đã sửa, ghi chú tuỳ chọn.</summary>
public sealed record ResolveChangeRequestRequest(string? Note);

/// <summary>Dùng cho mở lại (comment) và từ chối (lý do): đều bắt buộc.</summary>
public sealed record ChangeRequestCommentRequest(string? Comment);

public sealed record SubmissionDto(
    Guid Id,
    Guid AssignmentId,
    Guid ChapterId,
    Guid SubmittedByUserId,
    int AttemptNumber,
    SubmissionStatus Status,
    DateTime SubmittedAt,
    Guid? DecidedByUserId,
    DateTime? DecidedAt,
    string? DecisionComment)
{
    public static SubmissionDto From(Submission s) => new(
        s.Id, s.AssignmentId, s.ChapterId, s.SubmittedByUserId, s.AttemptNumber, s.Status, s.SubmittedAt,
        s.DecidedByUserId, s.DecidedAt, s.DecisionComment);
}

public sealed record ChangeRequestDto(
    Guid Id,
    Guid AssignmentId,
    Guid ChapterId,
    Guid? SubmissionId,
    Guid ResponsibleUserId,
    Guid CreatedByUserId,
    string Description,
    string? RegionJson,
    DateTime? DueDate,
    ChangeRequestStatus Status,
    string? ResolutionNote,
    DateTime? ResolvedAt,
    string? ReviewerComment,
    Guid? ClosedByUserId,
    DateTime? ClosedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public static ChangeRequestDto From(ChangeRequest c) => new(
        c.Id, c.AssignmentId, c.ChapterId, c.SubmissionId, c.ResponsibleUserId, c.CreatedByUserId, c.Description,
        c.RegionJson, c.DueDate, c.Status, c.ResolutionNote, c.ResolvedAt, c.ReviewerComment, c.ClosedByUserId,
        c.ClosedAt, c.CreatedAt, c.UpdatedAt);
}

/// <summary>PUT thay toàn bộ: DependsOnAssignmentId null = bỏ dependency; ReferenceNotes/DueDate null = xoá.</summary>
public sealed record UpdateAssignmentRequest(
    string Brief,
    string? ReferenceNotes,
    DateTime? DueDate,
    AssignmentPriority Priority,
    Guid AssigneeUserId,
    Guid? DependsOnAssignmentId);
