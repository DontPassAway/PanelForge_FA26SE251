using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Domain.Entities.Auth;

public class StudioWorkspace : BaseEntity
{
    public string Name { get; private set; } = default!;
    public Guid OwnerId { get; private set; }
    public long StorageQuotaBytes { get; private set; }
    public long UsedStorageBytes { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public User Owner { get; private set; } = default!;
    public ICollection<WorkspaceMember> Members { get; private set; } = [];
    public ICollection<Series> Series { get; private set; } = [];

    private StudioWorkspace() { }

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

    public void UpdateUsedStorage(long usedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(usedBytes);
        UsedStorageBytes = usedBytes;
    }

    public bool HasStorageCapacity(long additionalBytes)
    {
        if (StorageQuotaBytes == 0) return true;
        return UsedStorageBytes + additionalBytes <= StorageQuotaBytes;
    }
}
