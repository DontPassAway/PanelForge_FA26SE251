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

    public User User { get; private set; } = default!;

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, string tokenHash, DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new RefreshToken { UserId = userId, TokenHash = tokenHash, ExpiresAt = expiresAt };
    }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;

    public void Revoke() => RevokedAt ??= DateTime.UtcNow;
}
