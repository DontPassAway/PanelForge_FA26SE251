using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Preview;

/// <summary>
/// UC-21: phản hồi của người nhận link xem thử ở cấp chương, trang hoặc panel, gắn đúng phiên bản đang xem
/// (AssetReference của trang trong snapshot + version chương). UC-24: Producer/Editor triage.
/// </summary>
public class PreviewFeedback : BaseEntity
{
    public const int MaxMessageLength = 2000;

    public Guid PreviewLinkId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid? PageId { get; private set; }
    public Guid? PanelId { get; private set; }
    public Guid? AssetReferenceId { get; private set; }
    public long ChapterVersion { get; private set; }
    public string RecipientEmail { get; private set; } = default!;
    public string Message { get; private set; } = default!;
    public PreviewFeedbackStatus Status { get; private set; }
    public string? TriageNote { get; private set; }
    public Guid? TriagedByUserId { get; private set; }
    public DateTime? TriagedAt { get; private set; }

    private PreviewFeedback() { }

    public static PreviewFeedback Create(
        PreviewLink link, Guid? pageId, Guid? panelId, Guid? assetReferenceId, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Trim().Length > MaxMessageLength)
            throw new ArgumentException($"Phản hồi tối đa {MaxMessageLength} ký tự.", nameof(message));

        return new PreviewFeedback
        {
            PreviewLinkId = link.Id,
            ChapterId = link.ChapterId,
            PageId = pageId,
            PanelId = panelId,
            AssetReferenceId = assetReferenceId,
            ChapterVersion = link.ChapterVersion,
            RecipientEmail = link.RecipientEmail,
            Message = message.Trim(),
            Status = PreviewFeedbackStatus.New,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>UC-24: Noted hoặc NeedsChange; ghi chú bắt buộc với NeedsChange (để thành lý do mở lại / yêu cầu sửa).</summary>
    public void Triage(PreviewFeedbackStatus decision, string? note, Guid byUserId)
    {
        if (decision == PreviewFeedbackStatus.New)
            throw new ArgumentException("Chọn Noted hoặc NeedsChange.", nameof(decision));
        if (decision == PreviewFeedbackStatus.NeedsChange && string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("Cần sửa bắt buộc ghi chú (sẽ dùng làm lý do mở lại chương).", nameof(note));

        Status = decision;
        TriageNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        TriagedByUserId = byUserId;
        TriagedAt = DateTime.UtcNow;
        UpdatedAt = TriagedAt;
    }
}
