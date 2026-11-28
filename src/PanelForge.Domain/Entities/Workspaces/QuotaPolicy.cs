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
    /// <summary>Studio mới có bật AI sẵn hay không.</summary>
    public bool AiEnabledByDefault { get; private set; } = true;
    /// <summary>Ngưỡng "Gần hết" (%) ở màn Tài nguyên của Admin, 1..99.</summary>
    public int WarningThresholdPercent { get; private set; } = DefaultWarningThresholdPercent;
    public Guid ChangedByUserId { get; private set; }

    public const int DefaultWarningThresholdPercent = 80;

    private QuotaPolicy() { }

    /// <summary>
    /// Mọi mức phải dương: "0 = không giới hạn" chỉ Admin đặt riêng cho từng Studio, không phải mặc định cho mọi Studio.
    /// </summary>
    public static QuotaPolicy Create(
        long storageQuotaBytes, long monthlyTokenQuota, long monthlyImageQuota, Guid changedByUserId,
        bool aiEnabledByDefault = true, int warningThresholdPercent = DefaultWarningThresholdPercent)
    {
        if (warningThresholdPercent is < 1 or > 99)
            throw new ArgumentOutOfRangeException(nameof(warningThresholdPercent), "Ngưỡng cảnh báo phải từ 1 đến 99%.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storageQuotaBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monthlyTokenQuota);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monthlyImageQuota);
        if (changedByUserId == Guid.Empty) throw new ArgumentException("Chính sách phải có người sửa.", nameof(changedByUserId));

        return new QuotaPolicy
        {
            StorageQuotaBytes = storageQuotaBytes,
            MonthlyTokenQuota = monthlyTokenQuota,
            MonthlyImageQuota = monthlyImageQuota,
            AiEnabledByDefault = aiEnabledByDefault,
            WarningThresholdPercent = warningThresholdPercent,
            ChangedByUserId = changedByUserId
        };
    }
}
