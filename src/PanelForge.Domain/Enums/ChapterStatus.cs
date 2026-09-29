namespace PanelForge.Domain.Enums;

/// <summary>
/// E-02 Chapter state. Luồng chuyển trạng thái (Approve → Lock → Publish, Reopen) thuộc CF5 (UC-20, UC-23).
/// Chỉ chương Published mới xuất hiện trong public catalog (BR-23, BR-24).
/// </summary>
public enum ChapterStatus
{
    InProduction = 1,
    Approved = 2,
    Locked = 3,
    Published = 4
}
