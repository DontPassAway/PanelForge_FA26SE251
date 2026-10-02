namespace PanelForge.Domain.Enums;

public enum AiRunStatus
{
    Queued = 1,
    Running = 2,
    /// <summary>Dừng chờ con người: còn finding chặn sau số lượt sửa tối đa; Storyboard/Editor không chạy.</summary>
    WaitingForHuman = 3,
    /// <summary>Workflow chạy xong; các đề xuất đang chờ người quyết định.</summary>
    Completed = 4,
    Failed = 5,
    /// <summary>AI service chết/khởi động lại giữa chừng (không còn hoạt động quá hạn). Chưa hỗ trợ resume.</summary>
    Interrupted = 6
}
