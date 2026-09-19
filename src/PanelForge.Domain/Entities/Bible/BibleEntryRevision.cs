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
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public BibleEntry BibleEntry { get; private set; } = default!;

    private BibleEntryRevision() { }

    public static BibleEntryRevision Create(
        Guid bibleEntryId,
        int versionNumber,
        string summary,
        string snapshotJson,
        string contentHash,
        Guid? authorId = null,
        Guid? associatedChapterId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        return new BibleEntryRevision
        {
            BibleEntryId = bibleEntryId,
            VersionNumber = versionNumber,
            Summary = summary.Trim(),
            SnapshotJson = string.IsNullOrWhiteSpace(snapshotJson) ? "{}" : snapshotJson.Trim(),
            ContentHash = contentHash.Trim(),
            AuthorId = authorId,
            AssociatedChapterId = associatedChapterId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
