using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

/// <summary>Đánh giá 1–5 sao của độc giả cho một bộ truyện (mỗi người một đánh giá, sửa được).</summary>
public class SeriesRating : BaseEntity
{
    public const int MinStars = 1;
    public const int MaxStars = 5;

    public Guid SeriesId { get; private set; }
    public Guid UserId { get; private set; }
    public int Stars { get; private set; }

    /// <summary>Ngôn ngữ độc giả đang đọc khi đánh giá (để Publisher lọc theo ngôn ngữ).</summary>
    public string Language { get; private set; } = ContentLanguages.Original;

    private SeriesRating() { }

    public static SeriesRating Create(Guid seriesId, Guid userId, int stars, string language)
    {
        var rating = new SeriesRating { SeriesId = seriesId, UserId = userId };
        rating.Update(stars, language);
        return rating;
    }

    public void Update(int stars, string language)
    {
        if (stars is < MinStars or > MaxStars)
            throw new ArgumentOutOfRangeException(nameof(stars), "Đánh giá phải từ 1 đến 5 sao.");
        Stars = stars;
        Language = ContentLanguages.Normalize(language) ?? ContentLanguages.Original;
        UpdatedAt = DateTime.UtcNow;
    }
}
