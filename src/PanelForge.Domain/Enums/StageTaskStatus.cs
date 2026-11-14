namespace PanelForge.Domain.Enums;

/// <summary>Trạng thái một ô việc (Trang × Công đoạn) của lưới giao việc V3.</summary>
public enum StageTaskStatus
{
    Unassigned = 0,
    Assigned = 1,
    InProgress = 2,
    Submitted = 3,
    UnderReview = 4,
    Approved = 5,
    ReworkRequired = 6,
    Cancelled = 7
}
