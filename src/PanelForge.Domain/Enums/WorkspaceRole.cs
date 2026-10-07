namespace PanelForge.Domain.Enums;

public enum WorkspaceRole
{
    Producer,
    Writer,
    Artist,
    Letterer,
    Editor,
    /// <summary>Họp tuần 5: duyệt xuất bản và lên lịch phát hành chương đã được Producer duyệt. Lưu dạng chuỗi nên không cần migration.</summary>
    Publisher
}
