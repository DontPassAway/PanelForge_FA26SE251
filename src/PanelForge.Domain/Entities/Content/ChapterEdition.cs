using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// BR-34: Ấn bản ngôn ngữ của Chương (Language Edition).
/// Mỗi cặp (Chapter, LanguageCode) là 1 ấn bản được Editor thẩm định, Producer nghiệm thu và phát hành riêng.
/// </summary>
public class ChapterEdition : BaseEntity
{
    public Guid ChapterId { get; private set; }
    public string LanguageCode { get; private set; } = default!;
    public EditionStatus Status { get; private set; } = EditionStatus.Draft;
    public bool IsPaid { get; private set; } = false;
    public decimal PriceVnd { get; private set; } = 0m;
    public int FreePreviewPages { get; private set; } = 0;
    public Guid? CurrentSnapshotId { get; private set; }
    public Guid? TranslatorUserId { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public Guid? ApprovedById { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public long Version { get; private set; } = 0;

    public Chapter Chapter { get; private set; } = default!;
    public ChapterSnapshot? CurrentSnapshot { get; private set; }
    public User? TranslatorUser { get; private set; }

    private ChapterEdition() { }

    public static ChapterEdition Create(
        Guid chapterId, string languageCode, EditionStatus status = EditionStatus.Draft)
    {
        var norm = ContentLanguages.Normalize(languageCode)
            ?? throw new ArgumentException($"Ngôn ngữ '{languageCode}' không hợp lệ.", nameof(languageCode));

        return new ChapterEdition
        {
            ChapterId = chapterId,
            LanguageCode = norm,
            Status = status,
            Version = 0
        };
    }

    public void SubmitTranslation(Guid translatorUserId)
    {
        Status = EditionStatus.InReview;
        TranslatorUserId = translatorUserId;
        Version++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordTranslator(Guid translatorUserId)
    {
        TranslatorUserId = translatorUserId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ReviewApprove()
    {
        Status = EditionStatus.EditorApproved;
        Version++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ReviewReject()
    {
        Status = EditionStatus.Draft;
        Version++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Approve(Guid snapshotId, Guid approvedBy)
    {
        Status = EditionStatus.Approved;
        CurrentSnapshotId = snapshotId;
        ApprovedAt = DateTime.UtcNow;
        ApprovedById = approvedBy;
        Version++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Publish()
    {
        Status = EditionStatus.Published;
        PublishedAt = DateTime.UtcNow;
        Version++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reopen()
    {
        Status = EditionStatus.Draft;
        Version++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetMonetization(bool isPaid, decimal priceVnd, int freePreviewPages)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(priceVnd);
        ArgumentOutOfRangeException.ThrowIfNegative(freePreviewPages);
        IsPaid = isPaid;
        PriceVnd = isPaid ? priceVnd : 0m;
        FreePreviewPages = freePreviewPages;
        Version++;
        UpdatedAt = DateTime.UtcNow;
    }
}
