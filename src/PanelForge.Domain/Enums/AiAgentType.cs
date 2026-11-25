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
    Producer = 8,
    /// <summary>Copilot gợi ý metadata phát hành (logline, synopsis, thể loại, teaser, age rating).</summary>
    Publisher = 9
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
    Producer = 7,
    /// <summary>Copilot sync (một bước, một dòng thoại). Không nằm trong danh sách workflow batch.</summary>
    Copilot = 8,
    /// <summary>Sinh MỘT ảnh thumbnail cho panel từ prompt ảnh đã duyệt (sync, không nằm trong workflow batch).</summary>
    Thumbnail = 9
}
