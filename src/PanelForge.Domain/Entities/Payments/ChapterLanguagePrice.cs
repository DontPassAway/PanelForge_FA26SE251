using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Payments;

/// <summary>
/// Giá mở khóa chương trả phí theo từng ngôn ngữ do Publisher cấu hình.
/// Ngôn ngữ không có dòng giá riêng dùng <see cref="Chapter.PriceVnd"/>.
/// </summary>
public class ChapterLanguagePrice : BaseEntity
{
    public Guid ChapterId { get; private set; }
    public string Language { get; private set; } = default!;
    public decimal PriceVnd { get; private set; }

    public Chapter Chapter { get; private set; } = default!;

    private ChapterLanguagePrice() { }

    public static ChapterLanguagePrice Create(Guid chapterId, string language, decimal priceVnd)
    {
        if (!ContentLanguages.IsSupported(language))
            throw new ArgumentException("Ngôn ngữ không được hỗ trợ.", nameof(language));
        var price = new ChapterLanguagePrice { ChapterId = chapterId, Language = language };
        price.SetPrice(priceVnd);
        return price;
    }

    public void SetPrice(decimal priceVnd)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priceVnd);
        PriceVnd = priceVnd;
        UpdatedAt = DateTime.UtcNow;
    }
}
