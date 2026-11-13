namespace PanelForge.Domain.Enums;

/// <summary>
/// E-02 Chapter state. PreProduction (đang viết kịch bản) → InProduction tại Gate 1 (Start Production);
/// Approve → Lock → Publish, Reopen thuộc CF5 (UC-20, UC-23).
/// Chỉ chương Published mới xuất hiện trong public catalog (BR-23, BR-24).
/// </summary>
public enum ChapterStatus
{
    PreProduction = 0,
    InProduction = 1,
    Approved = 2,
    Locked = 3,
    Published = 4
}
