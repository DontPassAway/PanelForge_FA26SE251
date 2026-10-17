using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// CF4 / 4.1: manifest bất biến của chương tại lúc Producer duyệt. Append-only (BR-01): mỗi lần duyệt thêm một bản mới,
/// không sửa/xoá bản cũ. Chỉ lưu tham chiếu (page, artwork + SHA-256, revision script/Bible), không chép ảnh.
/// Xem thử, xuất file và phát hành trỏ vào snapshot thay vì trỏ vào chương đang có thể bị mở lại.
/// </summary>
public class ChapterSnapshot : BaseEntity
{
    public Guid ChapterId { get; private set; }
    /// <summary>Tăng dần theo Chapter, bắt đầu từ 1; unique (ChapterId, SnapshotNumber).</summary>
    public int SnapshotNumber { get; private set; }
    /// <summary>Chapter.VersionVector lúc chụp.</summary>
    public long ChapterVersion { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string ManifestJson { get; private set; } = default!;
    /// <summary>SHA-256 của ManifestJson để kiểm chứng manifest không bị đổi.</summary>
    public string ManifestHash { get; private set; } = default!;

    private ChapterSnapshot() { }

    public static ChapterSnapshot Create(Guid chapterId, int snapshotNumber, long chapterVersion,
        Guid createdByUserId, string manifestJson, string manifestHash)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshotNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestHash);

        return new ChapterSnapshot
        {
            ChapterId = chapterId,
            SnapshotNumber = snapshotNumber,
            ChapterVersion = chapterVersion,
            CreatedByUserId = createdByUserId,
            ManifestJson = manifestJson,
            ManifestHash = manifestHash
        };
    }
}
