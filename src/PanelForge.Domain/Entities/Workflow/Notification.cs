using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Thông báo trong ứng dụng gửi tới một người dùng (UC-14). Được ghi cùng lần lưu với thay đổi gây ra nó
/// nên không bao giờ có việc được giao mà thiếu thông báo (BR-14, BR-18).
/// </summary>
public class Notification : BaseEntity
{
    public Guid UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = default!;
    public string Message { get; private set; } = default!;
    public Guid? AssignmentId { get; private set; }
    public Guid? ChapterId { get; private set; }
    public DateTime? ReadAt { get; private set; }

    private Notification() { }

    public static Notification Create(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        Guid? assignmentId = null,
        Guid? chapterId = null)
    {
        if (userId == Guid.Empty) throw new ArgumentException("Thông báo phải có người nhận.", nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new Notification
        {
            UserId = userId,
            Type = type,
            Title = title.Trim(),
            Message = message.Trim(),
            AssignmentId = assignmentId,
            ChapterId = chapterId,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>Idempotent: giữ nguyên thời điểm đọc đầu tiên.</summary>
    public void MarkRead() => ReadAt ??= DateTime.UtcNow;
}
