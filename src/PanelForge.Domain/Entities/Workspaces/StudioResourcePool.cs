using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Workspaces;

/// <summary>
/// Kho tài nguyên của một Producer (D12, 2026-11-28): Admin bán dung lượng lưu trữ và token AI cho Producer,
/// Producer chia kho này cho các Studio mình sở hữu (<see cref="StudioWorkspace.StorageQuotaBytes"/>,
/// <see cref="WorkspaceAiConfig.TokenQuota"/>). Tổng phần đã chia không được vượt kho.
/// Producer chưa có kho (cấp quyền trước D12) giữ cách cũ: hạn mức Studio do Admin đặt.
/// </summary>
public class StudioResourcePool : BaseEntity
{
    /// <summary>xmin: hai lần chia tài nguyên cùng lúc chạm cùng kho → lần sau nhận 409, không cùng vượt kho.</summary>
    public uint RowVersion { get; private set; }

    public Guid OwnerId { get; private set; }
    public long StorageBytes { get; private set; }
    public long Tokens { get; private set; }

    private StudioResourcePool() { }

    public static StudioResourcePool Create(Guid ownerId, long storageBytes, long tokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(storageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        return new StudioResourcePool { OwnerId = ownerId, StorageBytes = storageBytes, Tokens = tokens };
    }

    /// <summary>Admin bán thêm: cộng dồn vào kho (chỉ cộng, không trừ, để không cắt phần Producer đã chia).</summary>
    public void Add(long storageBytes, long tokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(storageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        StorageBytes += storageBytes;
        Tokens += tokens;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Đánh dấu kho đã đổi khi chia lại cho Studio, để xmin bắt hai lần chia song song.</summary>
    public void MarkAllocationChanged() => UpdatedAt = DateTime.UtcNow;
}
