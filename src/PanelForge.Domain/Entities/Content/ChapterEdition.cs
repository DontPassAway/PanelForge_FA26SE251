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
    /// <summary>BR-33: ấn bản Trả phí (true) hay Free (false). Chỉ có nghĩa khi <see cref="IsMonetizationConfigured"/>.</summary>
    public bool IsPaid { get; private set; } = false;
    /// <summary>Giá mở khóa (VNĐ, số nguyên). Free = 0; Trả phí &gt; 0.</summary>
    public decimal PriceVnd { get; private set; } = 0m;
    /// <summary>N trang đầu người chưa mở khóa được xem (Trả phí: N &lt; tổng số trang). Free = 0.</summary>
    public int FreePreviewPages { get; private set; } = 0;

    /// <summary>
    /// D4: thời điểm Publisher chốt Free/Trả phí lần đầu. Null = chưa cấu hình → không được phát hành / hẹn giờ;
    /// không có mặc định "chưa cấu hình = Free".
    /// </summary>
    public DateTime? MonetizationConfiguredAt { get; private set; }
    public Guid? MonetizationConfiguredById { get; private set; }

    public bool IsMonetizationConfigured => MonetizationConfiguredAt.HasValue;
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

    /// <summary>
    /// BR-33 / D4: Publisher đặt loại Free/Trả phí, giá và N trang xem trước của ấn bản.
    /// Trả phí: <paramref name="priceVnd"/> &gt; 0. Free: giá và N về 0. Điều kiện N &lt; tổng số trang do handler kiểm
    /// (entity không biết số trang của chương).
    /// </summary>
    public void SetMonetization(bool isPaid, decimal priceVnd, int freePreviewPages, Guid configuredById)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(priceVnd);
        ArgumentOutOfRangeException.ThrowIfNegative(freePreviewPages);
        if (isPaid && priceVnd <= 0)
            throw new ArgumentOutOfRangeException(nameof(priceVnd), "Ấn bản trả phí phải có giá lớn hơn 0.");
        if (priceVnd != decimal.Truncate(priceVnd))
            throw new ArgumentOutOfRangeException(nameof(priceVnd), "Giá VNĐ phải là số nguyên đồng.");

        IsPaid = isPaid;
        PriceVnd = isPaid ? priceVnd : 0m;
        FreePreviewPages = isPaid ? freePreviewPages : 0;
        MonetizationConfiguredAt ??= DateTime.UtcNow;
        MonetizationConfiguredById ??= configuredById;
        Version++;
        UpdatedAt = DateTime.UtcNow;
    }
}
