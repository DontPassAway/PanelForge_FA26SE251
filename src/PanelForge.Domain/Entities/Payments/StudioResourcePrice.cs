using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Payments;

/// <summary>
/// Đơn giá tài nguyên Studio Admin bán cho Producer (D12): giá mỗi GB lưu trữ và mỗi 1 triệu token AI, mua một lần.
/// Append-only như <see cref="StudioPackagePrice"/>: bản ghi mới nhất là giá đang bán; chưa có bản ghi = chưa bán.
/// </summary>
public class StudioResourcePrice : BaseEntity
{
    public const int MinPriceVnd = 1_000;
    public const int MaxPriceVnd = 50_000_000;

    public int PricePerGbVnd { get; private set; }
    public int PricePerMillionTokensVnd { get; private set; }
    public Guid ChangedByUserId { get; private set; }

    private StudioResourcePrice() { }

    public static StudioResourcePrice Create(int pricePerGbVnd, int pricePerMillionTokensVnd, Guid changedByUserId)
    {
        if (pricePerGbVnd is < MinPriceVnd or > MaxPriceVnd || pricePerMillionTokensVnd is < MinPriceVnd or > MaxPriceVnd)
            throw new ArgumentOutOfRangeException(nameof(pricePerGbVnd),
                $"Đơn giá tài nguyên phải từ {MinPriceVnd:N0}đ đến {MaxPriceVnd:N0}đ.");

        return new StudioResourcePrice
        {
            PricePerGbVnd = pricePerGbVnd,
            PricePerMillionTokensVnd = pricePerMillionTokensVnd,
            ChangedByUserId = changedByUserId
        };
    }
}
