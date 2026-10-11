using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Identity;

/// <summary>
/// Refresh token đổi lấy access token mới. Chỉ lưu SHA-256 hash; xoay vòng mỗi lần dùng.
/// Token đã thu hồi mà bị dùng lại = nghi bị lộ → thu hồi toàn bộ token của user.
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    /// <summary>Một phiên đăng nhập = chuỗi token xoay vòng cùng SessionId; SessionStartedAt giữ nguyên qua các lần xoay (tính thời gian sống tuyệt đối).</summary>
    public Guid SessionId { get; private set; }
    public DateTime SessionStartedAt { get; private set; }

    public string? DeviceName { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTime LastActiveAt { get; private set; }

    public User User { get; private set; } = default!;

    private RefreshToken() { }

    /// <param name="sessionId">Null = phiên mới. Khi xoay vòng truyền SessionId và SessionStartedAt của token cũ.</param>
    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        DateTime expiresAt,
        Guid? sessionId = null,
        DateTime? sessionStartedAt = null,
        string? deviceName = null,
        string? ipAddress = null,
        string? userAgent = null,
        DateTime? lastActiveAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        var now = DateTime.UtcNow;
        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            SessionId = sessionId ?? Guid.NewGuid(),
            SessionStartedAt = sessionStartedAt ?? now,
            DeviceName = deviceName?.Trim(),
            IpAddress = ipAddress?.Trim(),
            UserAgent = userAgent?.Trim(),
            LastActiveAt = lastActiveAt ?? now
        };
    }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;

    public void Revoke() => RevokedAt ??= DateTime.UtcNow;
}
