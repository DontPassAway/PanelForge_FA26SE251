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
    DateTime? UpdatedAt,
    IReadOnlyList<Guid>? DependsOnAssignmentIds = null)
{
    public static AssignmentDto From(Assignment a) => new(
        a.Id, a.SeriesId, a.ChapterId, a.EntityType, a.TargetEntityId, a.PipelineStageId,
        a.AssigneeUserId, a.AssignedRole, a.Brief, a.ReferenceNotes, a.DueDate, a.Priority,
        a.Status, a.DependsOnAssignmentId, a.StartedAt, a.CompletedAt, a.LastFeedbackComment,
        a.CreatedAt, a.UpdatedAt, a.GetDependsOnAssignmentIds());
}

/// <summary>DTO dành cho màn hình làm việc cá nhân xuyên suốt toàn bộ Studio (Cross-Chapter Worklist).</summary>
public sealed record WorkspaceAssignmentDto(
    Guid Id,
    Guid SeriesId,
    string SeriesTitle,
    Guid ChapterId,
    decimal ChapterNumber,
    string? ChapterTitle,
    Guid PipelineStageId,
    string StageName,
    int StageOrder,
    string? StageColorCode,
    WorkflowEntityType EntityType,
    Guid TargetEntityId,
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
    DateTime? UpdatedAt,
    IReadOnlyList<Guid>? DependsOnAssignmentIds = null);

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
    Guid? DependsOnAssignmentId = null,
    IReadOnlyList<Guid>? DependsOnAssignmentIds = null);

/// <summary>Một yêu cầu sửa cụ thể. RegionJson tuỳ chọn: <c>{ "x", "y", "w", "h" }</c> chuẩn hóa 0–1.</summary>
/// <summary>AssetReferenceId: phiên bản artwork (của Chapter) mà RegionJson khoanh trên đó; PanelId: Panel của vùng (thuộc Chapter). Đều tuỳ chọn.</summary>
public sealed record ChangeRequestItemRequest(
    string Description, string? RegionJson = null, DateTime? DueDate = null, Guid? AssetReferenceId = null,
    Guid? PanelId = null);

/// <summary>
/// Trả về bắt buộc có comment (BR-06). Changes bỏ trống = tạo một ChangeRequest từ chính comment.
/// </summary>
public sealed record RequestAssignmentChangesRequest(
    string? Comment, IReadOnlyList<ChangeRequestItemRequest>? Changes = null, Guid? ExpectedSubmissionId = null);

/// <summary>Body tuỳ chọn khi duyệt: ExpectedSubmissionId = lần nộp Editor đang xem; lệch lần nộp đang chờ thì 409.</summary>
public sealed record ApproveAssignmentRequest(Guid? ExpectedSubmissionId = null);

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
    string? DecisionComment,
    IReadOnlyList<Guid>? ArtworkReferenceIds = null,  // phiên bản artwork (AssetReference.id) của lần nộp này
    long ChapterVersion = 0)                          // version chương lúc nộp (revision script)
{
    public static SubmissionDto From(Submission s) => new(
        s.Id, s.AssignmentId, s.ChapterId, s.SubmittedByUserId, s.AttemptNumber, s.Status, s.SubmittedAt,
        s.DecidedByUserId, s.DecidedAt, s.DecisionComment, s.ArtworkReferenceIds, s.ChapterVersion);
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
    DateTime? UpdatedAt,
    Guid? AssetReferenceId = null,  // phiên bản artwork mà vùng khoanh thuộc về
    Guid? PanelId = null)           // Panel của vùng khoanh
{
    public static ChangeRequestDto From(ChangeRequest c) => new(
        c.Id, c.AssignmentId, c.ChapterId, c.SubmissionId, c.ResponsibleUserId, c.CreatedByUserId, c.Description,
        c.RegionJson, c.DueDate, c.Status, c.ResolutionNote, c.ResolvedAt, c.ReviewerComment, c.ClosedByUserId,
        c.ClosedAt, c.CreatedAt, c.UpdatedAt, c.AssetReferenceId, c.PanelId);
}

/// <summary>PUT thay toàn bộ: DependsOnAssignmentId null = bỏ dependency; ReferenceNotes/DueDate null = xoá.</summary>
public sealed record UpdateAssignmentRequest(
    string Brief,
    string? ReferenceNotes,
    DateTime? DueDate,
    AssignmentPriority Priority,
    Guid AssigneeUserId,
    Guid? DependsOnAssignmentId,
    IReadOnlyList<Guid>? DependsOnAssignmentIds = null);
