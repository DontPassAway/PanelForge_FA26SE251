using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

/// <summary>Lịch sử đọc của độc giả: chương và trang đang đọc dở của một bộ truyện (mỗi bộ một dòng).</summary>
public class ReaderReadingProgress : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid ChapterId { get; private set; }
    public decimal ChapterNumber { get; private set; }
    public int LastPageNumber { get; private set; }
    public int TotalPages { get; private set; }
    public string Language { get; private set; } = ContentLanguages.Original;
    public DateTime ReadAt { get; private set; }

    private ReaderReadingProgress() { }

    public static ReaderReadingProgress Create(Guid userId, Guid seriesId) =>
        new() { UserId = userId, SeriesId = seriesId, ReadAt = DateTime.MinValue };

    /// <summary>
    /// Ghi tiến độ nếu mới hơn bản đang lưu (thiết bị nào đọc sau cùng thì thắng). Trả về true khi có thay đổi.
    /// </summary>
    public bool Apply(Guid chapterId, decimal chapterNumber, int lastPageNumber, int totalPages, string? language, DateTime readAt)
    {
        var utc = readAt.Kind == DateTimeKind.Utc ? readAt : readAt.ToUniversalTime();
        if (utc <= ReadAt) return false;
        ChapterId = chapterId;
        ChapterNumber = chapterNumber;
        TotalPages = Math.Max(0, totalPages);
        LastPageNumber = Math.Max(1, TotalPages > 0 ? Math.Min(lastPageNumber, TotalPages) : lastPageNumber);
        Language = ContentLanguages.Normalize(language) ?? ContentLanguages.Original;
        ReadAt = utc;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }
}
