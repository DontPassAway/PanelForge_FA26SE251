using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Identity;
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

    /// <summary>CF2: việc phải được Approved trước khi việc này bắt đầu (cùng Chapter, tối đa một; không được tạo chu trình).</summary>
    public Guid? DependsOnAssignmentId { get; private set; }

    /// <summary>Đồ thị phụ thuộc đa nhiệm (DAG): danh sách các công việc tiền đề phải được Approved trước khi việc này bắt đầu.</summary>
    public string DependsOnAssignmentIdsJson { get; private set; } = "[]";

    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? LastFeedbackComment { get; private set; }

    /// <summary>
    /// "Cần kiểm tra ảnh hưởng": đầu vào (stage trước / việc phụ thuộc) bị mở lại sau khi việc này đã dựa vào nó. Khác null thì
    /// không nộp/duyệt được, và stage sau không bắt đầu dựa trên nó, tới khi người duyệt quyết giữ kết quả hoặc làm lại.
    /// </summary>
    public string? ImpactReviewReason { get; private set; }
    public DateTime? ImpactReviewRequestedAt { get; private set; }

    /// <summary>Lý do Producer huỷ việc (bắt buộc); việc bị huỷ được xoá mềm, giữ lịch sử, không bao giờ tính là Approved.</summary>
    public string? CancelReason { get; private set; }

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
        AssignmentPriority priority = AssignmentPriority.Medium,
        Guid? dependsOnAssignmentId = null,
        IEnumerable<Guid>? dependsOnAssignmentIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brief);

        var depList = dependsOnAssignmentIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? [];

        if (dependsOnAssignmentId.HasValue && !depList.Contains(dependsOnAssignmentId.Value))
        {
            depList.Insert(0, dependsOnAssignmentId.Value);
        }

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
            DependsOnAssignmentId = depList.Count > 0 ? depList[0] : null,
            DependsOnAssignmentIdsJson = depList.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(depList) : "[]",
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

    /// <summary>CF3 bước 2: Editor mở việc đã nộp để review (Submitted → UnderReview).</summary>
    public void OpenReview()
    {
        if (Status != AssignmentStatus.Submitted)
            throw new InvalidOperationException($"Chỉ mở review được việc đã nộp, hiện: {Status}.");

        Status = AssignmentStatus.UnderReview;
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
    /// Phân công lại nhiệm vụ cho thành viên khác hoặc cập nhật vai trò. Đang làm dở (InProgress) mà đổi sang người khác thì lùi
    /// về Assigned: người mới phải bấm Bắt đầu để nhận bàn giao (bài đã tải lên vẫn giữ).
    /// </summary>
    public void Reassign(Guid newAssigneeUserId, WorkspaceRole newRole)
    {
        if (newAssigneeUserId != AssigneeUserId && Status == AssignmentStatus.InProgress)
            Status = AssignmentStatus.Assigned;
        AssigneeUserId = newAssigneeUserId;
        AssignedRole = newRole;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Đánh dấu cần kiểm tra ảnh hưởng (giữ nguyên trạng thái và kết quả cũ). Đã đánh dấu thì giữ lý do đầu tiên.</summary>
    public void FlagImpactReview(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (ImpactReviewReason is not null) return;
        ImpactReviewReason = reason.Trim();
        ImpactReviewRequestedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Người duyệt đã quyết (giữ kết quả hoặc làm lại): gỡ cờ. Quyết định và lý do nằm trong audit log.</summary>
    public void ClearImpactReview()
    {
        ImpactReviewReason = null;
        ImpactReviewRequestedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Ghi lý do huỷ trước khi xoá mềm. Việc đã Approved không huỷ được.</summary>
    public void Cancel(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status == AssignmentStatus.Approved)
            throw new InvalidOperationException("Không thể huỷ việc đã được duyệt (Approved).");
        CancelReason = reason.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Lấy danh sách toàn bộ ID của các công việc tiền đề phụ thuộc (DAG).</summary>
    public IReadOnlyList<Guid> GetDependsOnAssignmentIds()
    {
        var ids = new List<Guid>();
        if (!string.IsNullOrWhiteSpace(DependsOnAssignmentIdsJson) && DependsOnAssignmentIdsJson != "[]")
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(DependsOnAssignmentIdsJson);
                if (parsed != null) ids.AddRange(parsed.Where(i => i != Guid.Empty));
            }
            catch { }
        }

        if (DependsOnAssignmentId.HasValue && !ids.Contains(DependsOnAssignmentId.Value))
        {
            ids.Insert(0, DependsOnAssignmentId.Value);
        }

        return ids.Distinct().ToList();
    }

    /// <summary>Cập nhật đồ thị phụ thuộc đa nhiệm (DAG). Gán lại cả DependsOnAssignmentId đại diện.</summary>
    public void SetDependencies(IEnumerable<Guid>? dependsOnAssignmentIds)
    {
        var list = dependsOnAssignmentIds?
            .Where(id => id != Guid.Empty && id != Id)
            .Distinct()
            .ToList() ?? [];

        DependsOnAssignmentIdsJson = list.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(list) : "[]";
        DependsOnAssignmentId = list.Count > 0 ? list[0] : null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Đặt hoặc bỏ dependency đơn. Tự động đồng bộ vào danh sách đa nhiệm.</summary>
    public void SetDependency(Guid? dependsOnAssignmentId)
    {
        if (dependsOnAssignmentId == Id)
            throw new InvalidOperationException("Assignment không thể phụ thuộc vào chính nó.");

        SetDependencies(dependsOnAssignmentId.HasValue ? [dependsOnAssignmentId.Value] : null);
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
