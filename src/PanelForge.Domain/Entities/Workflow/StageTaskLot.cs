using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Lô giao việc của lưới V3: một người nhận một tập ô cùng công đoạn trong một chương, dùng chung brief và hạn.
/// Tách bảng riêng khỏi Assignment trong lúc mô hình cũ còn chạy song song, để code cũ không hiểu nhầm lô là việc cả chương.
/// </summary>
public class StageTaskLot : BaseEntity
{
    public const int MaxBriefLength = 2000;

    public Guid SeriesId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid PipelineStageId { get; private set; }
    public Guid AssigneeUserId { get; private set; }
    public WorkspaceRole AssignedRole { get; private set; }
    public string? Brief { get; private set; }
    public DateTime? DueDate { get; private set; }
    public StageTaskLotStatus Status { get; private set; } = StageTaskLotStatus.Active;
    public Guid AssignedByUserId { get; private set; }

    /// <summary>Concurrency token ánh xạ cột hệ thống <c>xmin</c> của PostgreSQL.</summary>
    public uint RowVersion { get; private set; }

    private StageTaskLot() { }

    public static StageTaskLot Create(
        Guid seriesId, Guid chapterId, Guid pipelineStageId, Guid assigneeUserId, WorkspaceRole assignedRole,
        Guid assignedByUserId, string? brief, DateTime? dueDate)
    {
        if (assigneeUserId == Guid.Empty) throw new ArgumentException("Thiếu người nhận.", nameof(assigneeUserId));
        var trimmed = string.IsNullOrWhiteSpace(brief) ? null : brief.Trim();
        if (trimmed?.Length > MaxBriefLength)
            throw new ArgumentException($"Brief tối đa {MaxBriefLength} ký tự.", nameof(brief));

        return new StageTaskLot
        {
            SeriesId = seriesId,
            ChapterId = chapterId,
            PipelineStageId = pipelineStageId,
            AssigneeUserId = assigneeUserId,
            AssignedRole = assignedRole,
            AssignedByUserId = assignedByUserId,
            Brief = trimmed,
            DueDate = dueDate,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Tính lại trạng thái theo các ô còn thuộc lô. Lô rỗng thì đóng hẳn; mọi ô đã Approved/Cancelled (có ít nhất một Approved)
    /// thì Completed; có ô bị trả về thì Completed quay lại Active. Lô Closed không mở lại.
    /// </summary>
    public void RefreshStatus(IReadOnlyCollection<StageTaskStatus> taskStatuses)
    {
        if (Status == StageTaskLotStatus.Closed) return;

        var next = taskStatuses.Count == 0 || taskStatuses.All(s => s == StageTaskStatus.Cancelled)
            ? StageTaskLotStatus.Closed
            : taskStatuses.All(s => s is StageTaskStatus.Approved or StageTaskStatus.Cancelled)
                ? StageTaskLotStatus.Completed
                : StageTaskLotStatus.Active;

        if (next == Status) return;
        Status = next;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Quá hạn chỉ khi còn ô chưa xong; lô Completed/Closed không bao giờ quá hạn.</summary>
    public bool IsOverdue(DateTime nowUtc) => Status == StageTaskLotStatus.Active && DueDate is { } due && due < nowUtc;
}
