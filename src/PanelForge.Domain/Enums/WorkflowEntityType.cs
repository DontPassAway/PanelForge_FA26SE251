namespace PanelForge.Domain.Enums;

/// <summary>
/// Định danh loại đối tượng chạy quy trình sản xuất (Trang truyện hoặc Từng khung tranh).
/// Phục vụ cơ chế theo dõi tiến độ ở cấp độ khung tranh (Panel-level progress tracking).
/// </summary>
public enum WorkflowEntityType
{
    Page = 1,
    Panel = 2
}
