namespace PanelForge.Domain.Enums;

/// <summary>
/// Vòng đời Finding (CF3 bước 5): Open chờ Editor triage → Confirmed / Rejected / Ignored / NeedsClarification.
/// Chạy kiểm tra lại: Confirmed không còn lỗi → Resolved; Open / NeedsClarification không còn lỗi → Stale (nguồn đã đổi).
/// </summary>
public enum ConsistencyFindingStatus
{
    Open = 1,
    Confirmed = 2,
    Rejected = 3,
    Ignored = 4,
    NeedsClarification = 5,
    Resolved = 6,
    Stale = 7
}
