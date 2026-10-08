namespace PanelForge.Domain.Enums;

/// <summary>UC-24: phản hồi từ link xem thử. New → Noted (ghi chú cho bản sau) | NeedsChange (cần sửa, Producer mở lại chương).</summary>
public enum PreviewFeedbackStatus
{
    New = 1,
    Noted = 2,
    NeedsChange = 3
}
