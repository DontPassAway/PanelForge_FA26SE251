using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Auth;

/// <summary>
/// Thực thể ghi nhận lịch sử kiểm toán toàn diện (BR-18, UC-15).
/// Mọi thao tác làm thay đổi dữ liệu ghi nhận Actor, Timestamp, Thay đổi.
/// </summary>
public class AuditLog : BaseEntity
{
    public Guid? WorkspaceId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? UserEmail { get; private set; }
    public string Action { get; private set; } = default!;
    public string EntityName { get; private set; } = default!;
    public string? EntityId { get; private set; }
    public string? ChangesJson { get; private set; }
    public DateTime TimestampUtc { get; private set; } = DateTime.UtcNow;
    public string? IpAddress { get; private set; }
    public string? Details { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        string action,
        string entityName,
        string? entityId = null,
        Guid? workspaceId = null,
        Guid? userId = null,
        string? userEmail = null,
        string? changesJson = null,
        string? ipAddress = null,
        string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        return new AuditLog
        {
            Action = action.Trim(),
            EntityName = entityName.Trim(),
            EntityId = entityId?.Trim(),
            WorkspaceId = workspaceId,
            UserId = userId,
            UserEmail = userEmail?.Trim(),
            ChangesJson = changesJson?.Trim(),
            TimestampUtc = DateTime.UtcNow,
            IpAddress = ipAddress?.Trim(),
            Details = details?.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
