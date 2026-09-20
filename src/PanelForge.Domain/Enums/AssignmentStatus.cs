namespace PanelForge.Domain.Enums;

/// <summary>
/// Trạng thái của một nhiệm vụ phân công (Assignment) trong quy trình sản xuất truyện tranh.
/// </summary>
public enum AssignmentStatus
{
    Assigned = 1,
    InProgress = 2,
    Submitted = 3,
    UnderReview = 4,
    Approved = 5,
    ReworkRequired = 6,
    Blocked = 7
}
