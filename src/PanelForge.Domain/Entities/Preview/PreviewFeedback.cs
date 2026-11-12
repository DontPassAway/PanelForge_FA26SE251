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
    public const int MaxReviewerNameLength = 80;

    public Guid PreviewLinkId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid? PageId { get; private set; }
    public Guid? PanelId { get; private set; }
    public Guid? AssetReferenceId { get; private set; }
    public long ChapterVersion { get; private set; }
    public string RecipientEmail { get; private set; } = default!;
    /// <summary>Tên người nhận xét tự khai (không bắt buộc) — link xem thử dùng chung cho nhiều cộng tác viên.</summary>
    public string? ReviewerName { get; private set; }
    public string Message { get; private set; } = default!;
    public PreviewFeedbackStatus Status { get; private set; }
    public string? TriageNote { get; private set; }
    public Guid? TriagedByUserId { get; private set; }
    public DateTime? TriagedAt { get; private set; }
    /// <summary>CF5 bước 7 / CF3: yêu cầu sửa sinh từ phản hồi "cần sửa"; null = chưa chuyển.</summary>
    public Guid? ChangeRequestId { get; private set; }

    private PreviewFeedback() { }

    public static PreviewFeedback Create(
        PreviewLink link, Guid? pageId, Guid? panelId, Guid? assetReferenceId, string message, string? reviewerName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Trim().Length > MaxMessageLength)
            throw new ArgumentException($"Phản hồi tối đa {MaxMessageLength} ký tự.", nameof(message));
        var name = string.IsNullOrWhiteSpace(reviewerName) ? null : reviewerName.Trim();
        if (name is { Length: > MaxReviewerNameLength })
            throw new ArgumentException($"Tên người nhận xét tối đa {MaxReviewerNameLength} ký tự.", nameof(reviewerName));

        return new PreviewFeedback
        {
            PreviewLinkId = link.Id,
            ChapterId = link.ChapterId,
            PageId = pageId,
            PanelId = panelId,
            AssetReferenceId = assetReferenceId,
            ChapterVersion = link.ChapterVersion,
            RecipientEmail = link.RecipientEmail,
            ReviewerName = name,
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

        if (ChangeRequestId.HasValue)
            throw new InvalidOperationException("Phản hồi đã thành yêu cầu sửa, không phân loại lại được.");

        Status = decision;
        TriageNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        TriagedByUserId = byUserId;
        TriagedAt = DateTime.UtcNow;
        UpdatedAt = TriagedAt;
    }

    /// <summary>Gắn yêu cầu sửa sinh từ phản hồi. Chỉ phản hồi NeedsChange, và mỗi phản hồi một lần.</summary>
    public void LinkChangeRequest(Guid changeRequestId)
    {
        if (Status != PreviewFeedbackStatus.NeedsChange)
            throw new InvalidOperationException("Chỉ phản hồi đã phân loại \"cần sửa\" mới chuyển thành yêu cầu sửa.");
        if (ChangeRequestId.HasValue)
            throw new InvalidOperationException("Phản hồi này đã được chuyển thành yêu cầu sửa.");

        ChangeRequestId = changeRequestId;
        UpdatedAt = DateTime.UtcNow;
    }
}
