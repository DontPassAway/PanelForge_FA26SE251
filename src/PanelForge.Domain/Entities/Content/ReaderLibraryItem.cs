using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

/// <summary>Một bộ truyện trong danh sách cá nhân của độc giả (Đọc sau / Theo dõi / Yêu thích).</summary>
public class ReaderLibraryItem : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid SeriesId { get; private set; }
    public ReaderLibraryList List { get; private set; }

    /// <summary>Theo dõi: lần cuối độc giả đã xem chương mới của bộ này (để báo chương mới trên mọi thiết bị).</summary>
    public DateTime? LastSeenAt { get; private set; }

    private ReaderLibraryItem() { }

    public static ReaderLibraryItem Create(Guid userId, Guid seriesId, ReaderLibraryList list, DateTime? lastSeenAt = null)
    {
        var item = new ReaderLibraryItem { UserId = userId, SeriesId = seriesId, List = list };
        item.MarkSeen(lastSeenAt ?? (list == ReaderLibraryList.Following ? DateTime.UtcNow : null));
        return item;
    }

    /// <summary>Chỉ tiến về sau: thiết bị cũ gửi mốc cũ hơn không làm hiện lại thông báo đã xem.</summary>
    public void MarkSeen(DateTime? at)
    {
        if (List != ReaderLibraryList.Following || at is null) return;
        var utc = at.Value.Kind == DateTimeKind.Utc ? at.Value : at.Value.ToUniversalTime();
        if (LastSeenAt is null || utc > LastSeenAt)
        {
            LastSeenAt = utc;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
