using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Workspaces;

/// <summary>
/// Chính sách hạn mức cấp cho Studio mới (OPS-20, BR-22). Append-only: mỗi lần Admin sửa là một bản ghi mới,
/// bản mới nhất là bản đang dùng; lịch sử giữ nguyên. Chỉ áp cho Studio tạo sau đó, không đổi Studio đã có.
/// </summary>
public class QuotaPolicy : BaseEntity
{
    public long StorageQuotaBytes { get; private set; }
    public long MonthlyTokenQuota { get; private set; }
    public long MonthlyImageQuota { get; private set; }
    public Guid ChangedByUserId { get; private set; }

    private QuotaPolicy() { }

    /// <summary>
    /// Mọi mức phải dương: "0 = không giới hạn" chỉ Admin đặt riêng cho từng Studio, không phải mặc định cho mọi Studio.
    /// </summary>
    public static QuotaPolicy Create(long storageQuotaBytes, long monthlyTokenQuota, long monthlyImageQuota, Guid changedByUserId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storageQuotaBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monthlyTokenQuota);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monthlyImageQuota);
        if (changedByUserId == Guid.Empty) throw new ArgumentException("Chính sách phải có người sửa.", nameof(changedByUserId));

        return new QuotaPolicy
        {
            StorageQuotaBytes = storageQuotaBytes,
            MonthlyTokenQuota = monthlyTokenQuota,
            MonthlyImageQuota = monthlyImageQuota,
            ChangedByUserId = changedByUserId
        };
    }
}
