using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Preview;

/// <summary>BR-17: mọi lần truy cập link xem thử, kể cả lần bị từ chối (token sai thì PreviewLinkId null). Chỉ thêm.</summary>
public class PreviewLinkAccessLog : BaseEntity
{
    public Guid? PreviewLinkId { get; private set; }
    /// <summary>Open | Image | Feedback.</summary>
    public string Action { get; private set; } = default!;
    public bool Allowed { get; private set; }
    /// <summary>Lý do từ chối: InvalidToken | Expired | Revoked | NotInSnapshot.</summary>
    public string? DenyReason { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTime AccessedAt { get; private set; }

    private PreviewLinkAccessLog() { }

    public static PreviewLinkAccessLog Create(Guid? previewLinkId, string action, bool allowed, string? denyReason, string? ipAddress)
    {
        var now = DateTime.UtcNow;
        return new PreviewLinkAccessLog
        {
            PreviewLinkId = previewLinkId,
            Action = action,
            Allowed = allowed,
            DenyReason = denyReason,
            IpAddress = ipAddress,
            AccessedAt = now,
            CreatedAt = now
        };
    }
}
