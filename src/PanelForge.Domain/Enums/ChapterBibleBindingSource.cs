namespace PanelForge.Domain.Enums;

/// <summary>
/// Nguồn gốc thực thể trong chương: Speaker (người nói) hoặc Highlight (bôi đen).
/// Một mục có cả hai nguồn thì giữ Speaker (BIB-11, Q16).
/// </summary>
public enum ChapterBibleBindingSource
{
    Speaker = 1,
    Highlight = 2
}
