namespace PanelForge.Domain.Common;

/// <summary>
/// Interface đánh dấu entity hỗ trợ logic xóa mềm (Soft-delete).
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
