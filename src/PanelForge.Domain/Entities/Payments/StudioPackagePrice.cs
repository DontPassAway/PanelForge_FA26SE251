using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Payments;

/// <summary>
/// Giá gói mở Studio do Admin đặt (spec D11). Append-only: mỗi lần đổi giá thêm một bản ghi, bản ghi mới nhất là giá
/// đang bán. Chưa có bản ghi nào thì gói chưa được bán.
/// </summary>
public class StudioPackagePrice : BaseEntity
{
    public const int MinPriceVnd = 1_000;
    public const int MaxPriceVnd = 50_000_000;

    public int PriceVnd { get; private set; }
    public Guid ChangedByUserId { get; private set; }

    private StudioPackagePrice() { }

    public static StudioPackagePrice Create(int priceVnd, Guid changedByUserId)
    {
        if (priceVnd is < MinPriceVnd or > MaxPriceVnd)
            throw new ArgumentOutOfRangeException(nameof(priceVnd),
                $"Giá gói mở Studio phải từ {MinPriceVnd:N0}đ đến {MaxPriceVnd:N0}đ.");

        return new StudioPackagePrice { PriceVnd = priceVnd, ChangedByUserId = changedByUserId };
    }
}
