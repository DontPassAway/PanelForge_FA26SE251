namespace PanelForge.Domain.Enums;

/// <summary>
/// Trạng thái của một Panel ở một stage (CF2 mục 6.3). Không có dòng StageProgress = NotStarted.
/// InReview / Approved / ChangesRequested do CF3 (cổng duyệt) chuyển.
/// </summary>
public enum StageProgressStatus
{
    InProgress = 1,
    Submitted = 2,
    InReview = 3,
    ChangesRequested = 4,
    Approved = 5
}
