using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Bible;

/// <summary>
/// Đại diện cho một thực thể cấu trúc trong Series Bible (Nhân vật, Địa điểm, Đạo cụ, Thuật ngữ, Quy tắc phong cách, Sự kiện cốt truyện).
/// </summary>
public class BibleEntry : BaseEntity
{
    public Guid SeriesBibleId { get; private set; }
    public BibleEntryCategory Category { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Subtitle { get; private set; }
    public string Description { get; private set; } = default!;
    public string DetailsJson { get; private set; } = "{}";
    public string? ReferenceImageUrl { get; private set; }
    public BiblePriority Priority { get; private set; } = BiblePriority.Standard;
    public bool StrictCheck { get; private set; } = true;
    public bool IsActive { get; private set; } = true;
    /// <summary>V2.5: Đánh dấu thực thể được tạo tạm thời từ cú pháp @-mention trong kịch bản (Write First, Bible Later).</summary>
    public bool IsStub { get; private set; } = false;

    public SeriesBible SeriesBible { get; private set; } = default!;
    public ICollection<BibleEntryRevision> Revisions { get; private set; } = [];

    private BibleEntry() { }

    public static BibleEntry Create(
        Guid seriesBibleId,
        BibleEntryCategory category,
        string code,
        string name,
        string description,
        string? subtitle = null,
        string? detailsJson = null,
        string? referenceImageUrl = null,
        BiblePriority priority = BiblePriority.Standard,
        bool strictCheck = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new BibleEntry
        {
            SeriesBibleId = seriesBibleId,
            Category = category,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Subtitle = subtitle?.Trim(),
            Description = description.Trim(),
            DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim(),
            ReferenceImageUrl = referenceImageUrl?.Trim(),
            Priority = priority,
            StrictCheck = strictCheck,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// V2.5: Tạo nhanh thực thể tạm (Stub) khi Writer gõ @-mention trong kịch bản.
    /// Cho phép Writer viết tự do (Write First, Bible Later).
    /// </summary>
    public static BibleEntry CreateStub(
        Guid seriesBibleId,
        BibleEntryCategory category,
        string name,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = name.Trim();
        var code = "STUB_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        return new BibleEntry
        {
            SeriesBibleId = seriesBibleId,
            Category = category,
            Code = code,
            Name = trimmedName,
            Description = description?.Trim() ?? $"Thực thể tạm thời từ kịch bản cho '@{trimmedName}'",
            DetailsJson = "{}",
            Priority = BiblePriority.Standard,
            StrictCheck = false,
            IsActive = true,
            IsStub = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdateDetails(
        string name,
        string description,
        string? subtitle,
        string? detailsJson,
        string? referenceImageUrl,
        BiblePriority priority,
        bool strictCheck)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Name = name.Trim();
        Description = description.Trim();
        Subtitle = subtitle?.Trim();
        DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim();
        ReferenceImageUrl = referenceImageUrl?.Trim();
        Priority = priority;
        StrictCheck = strictCheck;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsStub(bool isStub = true)
    {
        IsStub = isStub;
        UpdatedAt = DateTime.UtcNow;
    }

    public void PromoteFromStub(
        string code,
        string name,
        string description,
        string? subtitle = null,
        string? detailsJson = null,
        string? referenceImageUrl = null,
        BiblePriority priority = BiblePriority.Standard,
        bool strictCheck = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Description = description.Trim();
        Subtitle = subtitle?.Trim();
        DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim();
        ReferenceImageUrl = referenceImageUrl?.Trim();
        Priority = priority;
        StrictCheck = strictCheck;
        IsStub = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActiveStatus(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Danh mục có thiết kế hình (Nhân vật, Địa điểm, Đạo cụ): Artist nộp Concept → Editor duyệt → Producer khoá Canon,
    /// và AI chỉ dùng bản đã khoá. Thuật ngữ/Quy tắc/Sự kiện chỉ là văn bản, có hiệu lực ngay không cần khoá.
    /// </summary>
    public bool IsVisualDesign
        => Category is BibleEntryCategory.Character or BibleEntryCategory.Location or BibleEntryCategory.Prop;

    /// <summary>Đã có revision khoá bắt đầu đúng ở chương này: một revision mới ở cùng chương sẽ thay bản khoá tại chỗ (không cho phép).</summary>
    public bool HasLockedRevisionStartingAt(int chapterNumber)
        => Revisions.Any(r => r.IsLocked && r.EffectiveFromChapterNumber == chapterNumber);

    /// <summary>
    /// Giai đoạn được định danh bằng chương bắt đầu: mọi revision cùng EffectiveFromChapterNumber là các bản sửa của cùng một giai đoạn.
    /// Trả về bản mới nhất của giai đoạn bắt đầu ở chương này, null nếu chưa có giai đoạn nào ở đó.
    /// </summary>
    public BibleEntryRevision? LatestRevisionOfStageStartingAt(int chapterNumber)
        => Revisions
            .Where(r => r.EffectiveFromChapterNumber == chapterNumber)
            .MaxBy(r => r.VersionNumber);

    /// <summary>Bản mới nhất của từng giai đoạn, theo thứ tự chương.</summary>
    public IEnumerable<BibleEntryRevision> LatestRevisionPerStage()
        => Revisions
            .GroupBy(r => r.EffectiveFromChapterNumber)
            .OrderBy(g => g.Key)
            .Select(g => g.MaxBy(r => r.VersionNumber)!);

    public BibleEntryRevision AddRevision(
        string summary,
        string snapshotJson,
        string contentHash,
        Guid? authorId = null,
        Guid? associatedChapterId = null,
        int effectiveFromChapterNumber = 1,
        bool isInitialVersion = false,
        string? stageName = null,
        int? effectiveToChapterNumber = null,
        Guid? artistUserId = null,
        BibleRevisionStatus reviewStatus = BibleRevisionStatus.Draft,
        string? reworkFeedback = null)
    {
        var nextVersion = (Revisions.Count > 0) ? Revisions.Max(r => r.VersionNumber) + 1 : 1;
        var revision = BibleEntryRevision.Create(
            Id,
            nextVersion,
            summary,
            snapshotJson,
            contentHash,
            authorId,
            associatedChapterId,
            effectiveFromChapterNumber,
            isInitialVersion,
            stageName,
            effectiveToChapterNumber,
            artistUserId,
            reviewStatus,
            reworkFeedback);

        Revisions.Add(revision);
        UpdatedAt = DateTime.UtcNow;
        return revision;
    }

    public void RemoveRevision(BibleEntryRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        if (revision.IsLocked)
            throw new InvalidOperationException("Không thể xóa revision đã được khóa Canon.");

        if (revision.IsInitialVersion)
            throw new InvalidOperationException("Không thể xóa phiên bản gốc ban đầu (Initial Version).");

        if (revision.HasReviewHistory)
            throw new InvalidOperationException("Không thể xóa phiên bản đã có lịch sử nộp Concept/kiểm duyệt (append-only).");

        Revisions.Remove(revision);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Revision có hiệu lực tại một chương (BR-15): phạm vi [from, to] chứa chương (to null = tới hết truyện);
    /// nhiều bản cùng phủ thì lấy from lớn nhất (giai đoạn mới hơn) rồi đến VersionNumber lớn nhất (bản sửa mới hơn).
    /// Hết phạm vi mọi bản (vd. nhân vật đã rời truyện) → null.
    /// <paramref name="canonOnly"/>: với mục có thiết kế hình chỉ tính revision đã được Producer khoá (nguồn chuẩn cho AI).
    /// </summary>
    public BibleEntryRevision? EffectiveRevisionAt(int chapterNumber, bool canonOnly = false)
        => Revisions
            .Where(r => r.CoversChapter(chapterNumber))
            .Where(r => !canonOnly || !IsVisualDesign || r.IsLocked)
            .OrderByDescending(r => r.EffectiveFromChapterNumber)
            .ThenByDescending(r => r.VersionNumber)
            .FirstOrDefault();

    /// <summary>
    /// Revision đang có hiệu lực ở chương mới nhất (BR-15): EffectiveFromChapterNumber lớn nhất,
    /// cùng chương thì VersionNumber lớn nhất. Trạng thái hiện tại (Name, Description, ...) của entry
    /// phải luôn khớp với revision này; một bản sửa hồi tố (effective từ chương cũ hơn) không được ghi đè nó.
    /// </summary>
    public BibleEntryRevision? GetCurrentRevision()
        => Revisions
            .OrderByDescending(r => r.EffectiveFromChapterNumber)
            .ThenByDescending(r => r.VersionNumber)
            .FirstOrDefault();
}
