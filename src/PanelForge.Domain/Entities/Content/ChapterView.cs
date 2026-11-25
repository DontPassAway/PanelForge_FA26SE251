namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Một lượt đọc chương của độc giả (append-only), dùng để tính lượt xem thật và bảng xếp hạng Top Ngày/Tuần/Tháng.
/// <see cref="ViewerKey"/> = "u:{userId}" khi đăng nhập, "a:{sha256(ip)}" khi là khách — không lưu IP thô.
/// </summary>
public class ChapterView
{
    public const int MaxViewerKeyLength = 80;

    /// <summary>Cùng người xem đọc lại cùng chương trong khoảng này chỉ tính một lượt.</summary>
    public static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(30);

    public Guid Id { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid SeriesId { get; private set; }
    public string ViewerKey { get; private set; } = default!;
    public DateTime ViewedAt { get; private set; }

    /// <summary>Ngôn ngữ bản phát hành độc giả đọc.</summary>
    public string Language { get; private set; } = Enums.ContentLanguages.Original;

    private ChapterView() { }

    public static ChapterView Create(Guid chapterId, Guid seriesId, string viewerKey, DateTime viewedAt, string? language = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewerKey);
        if (viewerKey.Length > MaxViewerKeyLength)
            throw new ArgumentException("ViewerKey quá dài.", nameof(viewerKey));

        return new ChapterView
        {
            Id = Guid.NewGuid(),
            ChapterId = chapterId,
            SeriesId = seriesId,
            ViewerKey = viewerKey,
            ViewedAt = viewedAt,
            Language = Enums.ContentLanguages.Normalize(language) ?? Enums.ContentLanguages.Original
        };
    }
}
