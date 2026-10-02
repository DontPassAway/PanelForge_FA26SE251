namespace PanelForge.Domain.Enums;

/// <summary>Agent trong demo Writer → Continuity → Storyboard → Editor (Letterer/Artist/Producer để ngoài phạm vi demo).</summary>
public enum AiAgentType
{
    Writer = 1,
    Continuity = 2,
    Storyboard = 3,
    Editor = 4,
    /// <summary>Gemini làm điều phối viên: chọn agent chuyên môn tiếp theo trong phạm vi hàng rào của code.</summary>
    Supervisor = 5,
    /// <summary>Soạn prompt ảnh cho panel của layout đã duyệt (không sinh ảnh).</summary>
    Artist = 6,
    /// <summary>Đề xuất rút gọn thoại cho vừa bubble/text element.</summary>
    Letterer = 7,
    /// <summary>Viết báo cáo tiến độ từ snapshot do Backend tính.</summary>
    Producer = 8
}

/// <summary>Loại nhiệm vụ của một run; quyết định chuỗi agent hợp lệ (xem WorkflowRules). Run cũ đọc như FullScene.</summary>
public enum AiWorkflowType
{
    FullScene = 1,
    Script = 2,
    Storyboard = 3,
    Artist = 4,
    Letterer = 5,
    Editor = 6,
    Producer = 7
}
