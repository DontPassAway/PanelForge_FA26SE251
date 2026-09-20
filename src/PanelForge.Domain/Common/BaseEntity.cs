namespace PanelForge.Domain.Common;

/// <summary>
/// Lớp cơ sở cho toàn bộ Entity trong hệ thống, kế thừa IAuditableEntity và ISoftDelete.
/// </summary>
public abstract class BaseEntity : IAuditableEntity, ISoftDelete
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Soft-delete fields
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
