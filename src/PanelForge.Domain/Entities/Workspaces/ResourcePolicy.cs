using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Workspaces;

/// <summary>
/// Chính sách tài nguyên mặc định cấp nền tảng (một dòng duy nhất) do Administrator đặt.
/// Chỉ áp cho Studio TẠO MỚI; đổi chính sách không âm thầm đổi quota của Studio đã có.
/// 0 = không giới hạn (cùng quy ước với StorageQuotaBytes / MonthlyTokenQuota).
/// </summary>
public class ResourcePolicy : BaseEntity
{
    public const long DefaultStorageBytes = 20L * 1024 * 1024 * 1024; // 20 GB
    public const long DefaultTokens = 500_000;
    public const int DefaultWarningPercent = 80;

    public long DefaultStorageQuotaBytes { get; private set; }
    public long DefaultMonthlyTokenQuota { get; private set; }
    public long DefaultMonthlyImageQuota { get; private set; }
    public bool DefaultAiEnabled { get; private set; }
    /// <summary>Ngưỡng cảnh báo "Gần hết" (%) dùng cho bảng tài nguyên Admin và thanh mức dùng.</summary>
    public int WarningThresholdPercent { get; private set; }

    /// <summary>Concurrency token ánh xạ cột hệ thống <c>xmin</c> (BR-11).</summary>
    public uint RowVersion { get; private set; }

    private ResourcePolicy() { }

    /// <summary>Giá trị phát triển dùng khi chưa có chính sách nào được lưu.</summary>
    public static ResourcePolicy CreateDefault() => new()
    {
        DefaultStorageQuotaBytes = DefaultStorageBytes,
        DefaultMonthlyTokenQuota = DefaultTokens,
        DefaultMonthlyImageQuota = 0,
        DefaultAiEnabled = true,
        WarningThresholdPercent = DefaultWarningPercent,
        CreatedAt = DateTime.UtcNow
    };

    public void Update(long storageQuotaBytes, long monthlyTokenQuota, long monthlyImageQuota, bool aiEnabled, int warningThresholdPercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(storageQuotaBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(monthlyTokenQuota);
        ArgumentOutOfRangeException.ThrowIfNegative(monthlyImageQuota);
        if (warningThresholdPercent is < 1 or > 99)
            throw new ArgumentOutOfRangeException(nameof(warningThresholdPercent), "Ngưỡng cảnh báo phải từ 1 đến 99%.");

        DefaultStorageQuotaBytes = storageQuotaBytes;
        DefaultMonthlyTokenQuota = monthlyTokenQuota;
        DefaultMonthlyImageQuota = monthlyImageQuota;
        DefaultAiEnabled = aiEnabled;
        WarningThresholdPercent = warningThresholdPercent;
        UpdatedAt = DateTime.UtcNow;
    }
}
