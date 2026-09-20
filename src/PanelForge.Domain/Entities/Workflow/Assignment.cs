using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Đại diện cho một nhiệm vụ phân công (Assignment) trong hàng đợi tác vụ của nghệ sĩ/người viết/người chèn chữ (Artist/Writer/Letterer).
/// Quản lý yêu cầu brief, tư liệu tham chiếu Series Bible, hạn chót, và vòng đời chuyển trạng thái tác vụ.
/// </summary>
public class Assignment : BaseEntity
{
    public Guid SeriesId { get; private set; }
    public Guid ChapterId { get; private set; }
    public WorkflowEntityType EntityType { get; private set; }
    public Guid TargetEntityId { get; private set; }
    public Guid PipelineStageId { get; private set; }
    public Guid AssigneeUserId { get; private set; }
    public WorkspaceRole AssignedRole { get; private set; }

    public string Brief { get; private set; } = default!;
    public string? ReferenceNotes { get; private set; }
    public string ReferenceAssetUrlsJson { get; private set; } = "[]";

    public DateTime? DueDate { get; private set; }
    public AssignmentPriority Priority { get; private set; } = AssignmentPriority.Medium;
    public AssignmentStatus Status { get; private set; } = AssignmentStatus.Assigned;

    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? LastFeedbackComment { get; private set; }

    public Series Series { get; private set; } = default!;
    public Chapter Chapter { get; private set; } = default!;
    public PipelineStage Stage { get; private set; } = default!;
    public User Assignee { get; private set; } = default!;

    private Assignment() { }

    public static Assignment Create(
        Guid seriesId,
        Guid chapterId,
        WorkflowEntityType entityType,
        Guid targetEntityId,
        Guid pipelineStageId,
        Guid assigneeUserId,
        WorkspaceRole assignedRole,
        string brief,
        string? referenceNotes = null,
        string? referenceAssetUrlsJson = null,
        DateTime? dueDate = null,
        AssignmentPriority priority = AssignmentPriority.Medium)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brief);

        return new Assignment
        {
            SeriesId = seriesId,
            ChapterId = chapterId,
            EntityType = entityType,
            TargetEntityId = targetEntityId,
            PipelineStageId = pipelineStageId,
            AssigneeUserId = assigneeUserId,
            AssignedRole = assignedRole,
            Brief = brief.Trim(),
            ReferenceNotes = referenceNotes?.Trim(),
            ReferenceAssetUrlsJson = string.IsNullOrWhiteSpace(referenceAssetUrlsJson) ? "[]" : referenceAssetUrlsJson.Trim(),
            DueDate = dueDate,
            Priority = priority,
            Status = AssignmentStatus.Assigned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Bắt đầu thực hiện tác vụ (chuyển sang InProgress).
    /// </summary>
    public void StartWork()
    {
        if (Status != AssignmentStatus.Assigned && Status != AssignmentStatus.ReworkRequired)
            throw new InvalidOperationException($"Không thể bắt đầu tác vụ đang ở trạng thái {Status}.");

        Status = AssignmentStatus.InProgress;
        StartedAt ??= DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Nộp bản thảo/bản vẽ để đưa vào quy trình Review.
    /// </summary>
    public void SubmitForReview()
    {
        if (Status != AssignmentStatus.InProgress && Status != AssignmentStatus.Assigned)
            throw new InvalidOperationException($"Không thể nộp tác vụ đang ở trạng thái {Status}.");

        Status = AssignmentStatus.Submitted;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Đánh dấu yêu cầu làm lại (Rework) kèm ghi chú phản hồi từ Editor/Reviewer.
    /// </summary>
    public void RequestRework(string feedbackComment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedbackComment);

        Status = AssignmentStatus.ReworkRequired;
        LastFeedbackComment = feedbackComment.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Đánh dấu phê duyệt hoàn thành nhiệm vụ.
    /// </summary>
    public void MarkApproved()
    {
        Status = AssignmentStatus.Approved;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Phân công lại nhiệm vụ cho thành viên khác hoặc cập nhật vai trò.
    /// </summary>
    public void Reassign(Guid newAssigneeUserId, WorkspaceRole newRole)
    {
        AssigneeUserId = newAssigneeUserId;
        AssignedRole = newRole;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Cập nhật nội dung tóm tắt brief, tư liệu tham khảo, hạn chót và độ ưu tiên.
    /// </summary>
    public void UpdateBriefAndDeadline(
        string brief,
        string? referenceNotes,
        string? referenceAssetUrlsJson,
        DateTime? dueDate,
        AssignmentPriority priority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brief);

        Brief = brief.Trim();
        ReferenceNotes = referenceNotes?.Trim();
        ReferenceAssetUrlsJson = string.IsNullOrWhiteSpace(referenceAssetUrlsJson) ? "[]" : referenceAssetUrlsJson.Trim();
        DueDate = dueDate;
        Priority = priority;
        UpdatedAt = DateTime.UtcNow;
    }
}
