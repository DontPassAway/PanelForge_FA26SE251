using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Workspaces;

/// <summary>
/// Giới hạn kích thước một file tải lên, Admin đặt cho toàn nền tảng (GĐ3 plan 10). Append-only như <see cref="QuotaPolicy"/>:
/// mỗi lần sửa là một bản ghi mới, bản mới nhất là bản đang dùng. Khác chính sách hạn mức, giới hạn này áp ngay cho mọi Studio.
/// Mỗi mức nằm trong (0, trần): trần là giới hạn request của server, không đổi được lúc chạy.
/// </summary>
public class UploadLimitPolicy : BaseEntity
{
    public const long MiB = 1024 * 1024;

    /// <summary>Trần cho artwork (trang/panel, ảnh tách panel). Khớp <c>RequestSizeLimit</c> ở controller.</summary>
    public const long ArtworkCeilingBytes = 50 * MiB;
    /// <summary>Trần cho ảnh bìa, avatar/banner và ảnh tham chiếu Bible.</summary>
    public const long ImageCeilingBytes = 20 * MiB;

    public const long DefaultArtworkMaxBytes = 25 * MiB;
    public const long DefaultReferenceImageMaxBytes = 10 * MiB;
    public const long DefaultCoverImageMaxBytes = 5 * MiB;

    /// <summary>Artwork trang/panel và ảnh trang gửi đi tách panel.</summary>
    public long ArtworkMaxBytes { get; private set; }
    /// <summary>Ảnh tham chiếu (reference sheet) của Bible.</summary>
    public long ReferenceImageMaxBytes { get; private set; }
    /// <summary>Bìa Series, avatar/banner Series, bìa Chương, bìa Studio.</summary>
    public long CoverImageMaxBytes { get; private set; }
    public Guid ChangedByUserId { get; private set; }

    private UploadLimitPolicy() { }

    public static UploadLimitPolicy Create(
        long artworkMaxBytes, long referenceImageMaxBytes, long coverImageMaxBytes, Guid changedByUserId)
    {
        EnsureInRange(artworkMaxBytes, ArtworkCeilingBytes, nameof(artworkMaxBytes));
        EnsureInRange(referenceImageMaxBytes, ImageCeilingBytes, nameof(referenceImageMaxBytes));
        EnsureInRange(coverImageMaxBytes, ImageCeilingBytes, nameof(coverImageMaxBytes));
        if (changedByUserId == Guid.Empty) throw new ArgumentException("Giới hạn upload phải có người sửa.", nameof(changedByUserId));

        return new UploadLimitPolicy
        {
            ArtworkMaxBytes = artworkMaxBytes,
            ReferenceImageMaxBytes = referenceImageMaxBytes,
            CoverImageMaxBytes = coverImageMaxBytes,
            ChangedByUserId = changedByUserId
        };
    }

    private static void EnsureInRange(long value, long ceiling, string name)
    {
        if (value <= 0 || value > ceiling)
            throw new ArgumentOutOfRangeException(name, $"Giới hạn phải lớn hơn 0 và không quá {ceiling / MiB} MB.");
    }
}
