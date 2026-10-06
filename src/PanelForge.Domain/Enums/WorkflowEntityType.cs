namespace PanelForge.Domain.Enums;

/// <summary>
/// Định danh loại đối tượng chạy quy trình sản xuất (Trang truyện hoặc Từng khung tranh).
/// Phục vụ cơ chế theo dõi tiến độ ở cấp độ khung tranh (Panel-level progress tracking).
/// </summary>
public enum WorkflowEntityType
{
    Page = 1,
    Panel = 2,
    /// <summary>CF2 bước 1: giao việc cho cả Chapter (TargetEntityId = ChapterId). Cột lưu dạng chuỗi nên không cần migration.</summary>
    Chapter = 3
}
