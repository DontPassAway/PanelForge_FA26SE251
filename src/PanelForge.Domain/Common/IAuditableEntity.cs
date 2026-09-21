namespace PanelForge.Domain.Common;

/// <summary>
/// Interface đánh dấu các entity có theo dõi thời gian và danh tính người tạo / cập nhật.
/// </summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}
