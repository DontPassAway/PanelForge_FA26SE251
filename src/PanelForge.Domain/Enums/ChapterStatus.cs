namespace PanelForge.Domain.Enums;

/// <summary>
/// V2.6 Chapter state (REL-01, Glossary §3.1).
/// PreProduction (soạn kịch bản) → InProduction tại Gate 1 (Start Production);
/// ChapterApproved tại Gate 3 (Producer duyệt toàn bộ tranh);
/// HandedOffAndPublished khi Publisher phát hành ấn bản (bản cuối cùng).
/// </summary>
public enum ChapterStatus
{
    PreProduction = 0,
    InProduction = 1,
    ChapterApproved = 2,
    HandedOffAndPublished = 3
}
