using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Bible;

/// <summary>
/// Lưu trữ lịch sử phiên bản bất biến (append-only provenance) của từng mục trong Series Bible.
/// Đảm bảo tính toàn vẹn (Integrity of the Revision Record) và truy vết nguồn gốc (Full Revision Provenance).
/// </summary>
public class BibleEntryRevision : BaseEntity
{
    public Guid BibleEntryId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Summary { get; private set; } = default!;
    public string SnapshotJson { get; private set; } = "{}";
    public Guid? AuthorId { get; private set; }
    public string ContentHash { get; private set; } = default!;
    public Guid? AssociatedChapterId { get; private set; }
    public int EffectiveFromChapterNumber { get; private set; } = 1;

    /// <summary>
    /// Chương cuối cùng revision còn hiệu lực (gồm cả chương này); null = tới hết truyện.
    /// Dùng khi một giai đoạn kết thúc mà không có giai đoạn nối tiếp (vd. nhân vật rời truyện ở chương 50).
    /// Giai đoạn nối tiếp (from lớn hơn) vẫn được ưu tiên trong phần chồng lấn, nên không bắt buộc đặt.
    /// </summary>
    public int? EffectiveToChapterNumber { get; private set; }

    /// <summary>Tên giai đoạn do người dùng đặt, vd. "Lúc nhỏ", "Trưởng thành". Không phải số phiên bản.</summary>
    public string? StageName { get; private set; }

    public bool IsInitialVersion { get; private set; } = false;

    public bool CoversChapter(int chapterNumber)
        => EffectiveFromChapterNumber <= chapterNumber
           && (EffectiveToChapterNumber is null || chapterNumber <= EffectiveToChapterNumber);

    /// <summary>V1.2: Producer đã duyệt và khoá revision này làm tham chiếu. Chỉ thêm metadata, nội dung (snapshot, hash) vẫn bất biến; không có thao tác mở khoá.</summary>
    public DateTime? LockedAt { get; private set; }
    public Guid? LockedById { get; private set; }
    public bool IsLocked => LockedAt.HasValue;

    public BibleEntry BibleEntry { get; private set; } = default!;

    private BibleEntryRevision() { }

    public void Lock(Guid userId)
    {
        if (IsLocked) throw new InvalidOperationException("Revision đã được khoá.");
        LockedAt = DateTime.UtcNow;
        LockedById = userId;
    }

    public static BibleEntryRevision Create(
        Guid bibleEntryId,
        int versionNumber,
        string summary,
        string snapshotJson,
        string contentHash,
        Guid? authorId = null,
        Guid? associatedChapterId = null,
        int effectiveFromChapterNumber = 1,
        bool isInitialVersion = false,
        string? stageName = null,
        int? effectiveToChapterNumber = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        var from = Math.Max(1, effectiveFromChapterNumber);
        if (effectiveToChapterNumber < from)
            throw new ArgumentException($"Chương kết thúc hiệu lực ({effectiveToChapterNumber}) phải ≥ chương bắt đầu ({from}).");
        if (stageName?.Trim().Length > 100)
            throw new ArgumentException("Tên giai đoạn tối đa 100 ký tự.");

        return new BibleEntryRevision
        {
            BibleEntryId = bibleEntryId,
            VersionNumber = versionNumber,
            Summary = summary.Trim(),
            SnapshotJson = string.IsNullOrWhiteSpace(snapshotJson) ? "{}" : snapshotJson.Trim(),
            ContentHash = contentHash.Trim(),
            AuthorId = authorId,
            AssociatedChapterId = associatedChapterId,
            EffectiveFromChapterNumber = from,
            EffectiveToChapterNumber = effectiveToChapterNumber,
            StageName = string.IsNullOrWhiteSpace(stageName) ? null : stageName.Trim(),
            IsInitialVersion = isInitialVersion,
            CreatedAt = DateTime.UtcNow
        };
    }
}
