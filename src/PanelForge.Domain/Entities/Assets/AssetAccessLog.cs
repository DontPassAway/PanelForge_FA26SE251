using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Assets;

/// <summary>Nhật ký mỗi lần tải artwork chưa phát hành qua link ngắn hạn (BR-16). Chỉ thêm.</summary>
public class AssetAccessLog : BaseEntity
{
    public Guid AssetReferenceId { get; private set; }
    public Guid UserId { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTime AccessedAt { get; private set; }

    private AssetAccessLog() { }

    public static AssetAccessLog Create(Guid assetReferenceId, Guid userId, string? ipAddress) => new()
    {
        AssetReferenceId = assetReferenceId,
        UserId = userId,
        IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim(),
        AccessedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };
}
