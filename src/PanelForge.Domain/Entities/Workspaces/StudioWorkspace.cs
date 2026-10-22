using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Domain.Entities.Workspaces;

public class StudioWorkspace : BaseEntity
{
    public string Name { get; private set; } = default!;
    public Guid OwnerId { get; private set; }
    public long StorageQuotaBytes { get; private set; }
    public long UsedStorageBytes { get; private set; }

    /// <summary>V1.2 session policy do Administrator đặt; null = không giới hạn ở Workspace này. User thuộc nhiều Workspace chịu giá trị chặt nhất.</summary>
    public int? SessionLifetimeMinutes { get; private set; }
    public int? SessionIdleTimeoutMinutes { get; private set; }
    public int? MaxConcurrentSessions { get; private set; }

    public const int MaxSessionLifetimeMinutes = 30 * 24 * 60;

    /// <summary>SHA-256 của ảnh bìa Studio lưu trong IAssetStorage (R2); null nếu dùng bìa mặc định.</summary>
    public string? CoverImageSha256 { get; private set; }

    public User Owner { get; private set; } = default!;
    public ICollection<WorkspaceMember> Members { get; private set; } = [];
    public ICollection<Series> Series { get; private set; } = [];

    private StudioWorkspace() { }

    public void SetCover(string? sha256) => CoverImageSha256 = sha256;

    public static StudioWorkspace Create(string name, Guid ownerId, long storageQuotaBytes = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(storageQuotaBytes);

        return new StudioWorkspace
        {
            Name = name.Trim(),
            OwnerId = ownerId,
            StorageQuotaBytes = storageQuotaBytes,
            UsedStorageBytes = 0
        };
    }

    public void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Name = newName.Trim();
    }

    public void SetStorageQuota(long quotaBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quotaBytes);
        StorageQuotaBytes = quotaBytes;
    }

    /// <summary>Thay toàn bộ session policy (null = bỏ giới hạn đó). Lifetime tối đa 30 ngày (bằng hạn refresh token).</summary>
    public void SetSessionPolicy(int? lifetimeMinutes, int? idleTimeoutMinutes, int? maxConcurrentSessions)
    {
        if (lifetimeMinutes is < 5 or > MaxSessionLifetimeMinutes)
            throw new ArgumentOutOfRangeException(nameof(lifetimeMinutes), $"Thời gian sống của phiên phải từ 5 đến {MaxSessionLifetimeMinutes} phút.");
        if (idleTimeoutMinutes is < 1)
            throw new ArgumentOutOfRangeException(nameof(idleTimeoutMinutes), "Idle timeout phải từ 1 phút trở lên.");
        if (lifetimeMinutes.HasValue && idleTimeoutMinutes > lifetimeMinutes)
            throw new ArgumentOutOfRangeException(nameof(idleTimeoutMinutes), "Idle timeout không được lớn hơn thời gian sống của phiên.");
        if (maxConcurrentSessions is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentSessions), "Số phiên đồng thời phải từ 1 đến 100.");

        SessionLifetimeMinutes = lifetimeMinutes;
        SessionIdleTimeoutMinutes = idleTimeoutMinutes;
        MaxConcurrentSessions = maxConcurrentSessions;
    }

    public void UpdateUsedStorage(long usedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(usedBytes);
        UsedStorageBytes = usedBytes;
    }

    /// <summary>Giữ chỗ dung lượng cho dữ liệu sắp lưu. Trả false và không đổi gì nếu vượt quota (quota 0 = không giới hạn).</summary>
    public bool TryReserveStorage(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (!HasStorageCapacity(bytes)) return false;
        UsedStorageBytes += bytes;
        return true;
    }

    /// <summary>Trả lại dung lượng khi dữ liệu bị xoá; không xuống dưới 0.</summary>
    public void ReleaseStorage(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        UsedStorageBytes = Math.Max(0, UsedStorageBytes - bytes);
    }

    public bool HasStorageCapacity(long additionalBytes)
    {
        if (StorageQuotaBytes == 0) return true;
        return UsedStorageBytes + additionalBytes <= StorageQuotaBytes;
    }
}
