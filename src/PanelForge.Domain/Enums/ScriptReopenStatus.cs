namespace PanelForge.Domain.Enums;

/// <summary>
/// V2.5 & WF04: Trạng thái của quy trình yêu cầu mở lại kịch bản đã đóng băng (ScriptReopen Governance).
/// </summary>
public enum ScriptReopenStatus
{
    None = 0,
    Requested = 1,
    Approved = 2,
    Rejected = 3
}

