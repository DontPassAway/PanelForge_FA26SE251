using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Auth;

/// <summary>
/// Đại diện cho một thiết bị tin cậy của người dùng được ghi nhớ trong vòng 30 ngày.
/// Lưu trữ SHA-256 hash của token để đảm bảo an toàn nếu Database bị lộ.
/// </summary>
public class UserRememberedDevice : BaseEntity
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public string? DeviceName { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    public User User { get; private set; } = default!;

    private UserRememberedDevice() { }

    public static UserRememberedDevice Create(Guid userId, string tokenHash, DateTime expiresAt, string? deviceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new UserRememberedDevice
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            DeviceName = deviceName?.Trim()
        };
    }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}
