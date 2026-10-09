using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Một quyết định duyệt cấp Chapter (Producer duyệt, Publisher duyệt/từ chối, mở lại). Chỉ thêm, không sửa hay xoá,
/// nên lịch sử phê duyệt được giữ nguyên (BR-01). <see cref="ChapterVersion"/> là VersionVector lúc quyết định.
/// </summary>
public class ChapterReview : BaseEntity
{
    public Guid ChapterId { get; private set; }
    public ChapterReviewAction Action { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string? Comment { get; private set; }
    /// <summary>Chỉ có với <see cref="ChapterReviewAction.PublisherPublished"/>: thời điểm Reader bắt đầu thấy chương.</summary>
    public DateTime? ScheduledAt { get; private set; }
    public long ChapterVersion { get; private set; }

    private ChapterReview() { }

    public static ChapterReview Create(
        Guid chapterId, ChapterReviewAction action, Guid actorUserId, long chapterVersion,
        string? comment = null, DateTime? scheduledAt = null)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("Quyết định phải có người thực hiện.", nameof(actorUserId));

        return new ChapterReview
        {
            ChapterId = chapterId,
            Action = action,
            ActorUserId = actorUserId,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            ScheduledAt = scheduledAt,
            ChapterVersion = chapterVersion,
            CreatedAt = DateTime.UtcNow
        };
    }
}
