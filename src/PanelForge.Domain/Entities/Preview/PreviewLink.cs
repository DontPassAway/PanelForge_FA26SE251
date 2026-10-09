using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Preview;

/// <summary>
/// UC-11, BR-09, BR-17: link xem thử cho MỘT email người nhận, có hạn, thu hồi được, chỉ cho Chapter đã duyệt (khoá).
/// Chỉ lưu SHA-256 của token (token gốc trả một lần lúc tạo). Gắn snapshot: các phiên bản artwork của từng trang lúc tạo link,
/// nên người nhận thấy đúng bản đã duyệt dù sau đó có thay đổi.
/// </summary>
public class PreviewLink : BaseEntity
{
    public Guid ChapterId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string RecipientEmail { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public string? RevokeReason { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    /// <summary>AssetReference của từng trang (bản ở stage cuối, mới nhất) lúc tạo link.</summary>
    public List<Guid> SnapshotArtworkIds { get; private set; } = [];
    /// <summary>Chapter.VersionVector lúc tạo link.</summary>
    public long ChapterVersion { get; private set; }

    private PreviewLink() { }

    public static PreviewLink Create(
        Guid chapterId, Guid workspaceId, string recipientEmail, string tokenHash, DateTime expiresAt,
        Guid createdByUserId, IEnumerable<Guid> snapshotArtworkIds, long chapterVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (expiresAt <= DateTime.UtcNow) throw new ArgumentException("Hạn của link phải ở tương lai.", nameof(expiresAt));

        return new PreviewLink
        {
            ChapterId = chapterId,
            WorkspaceId = workspaceId,
            RecipientEmail = recipientEmail.Trim().ToLowerInvariant(),
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedByUserId = createdByUserId,
            SnapshotArtworkIds = snapshotArtworkIds.Distinct().ToList(),
            ChapterVersion = chapterVersion,
            CreatedAt = DateTime.UtcNow
        };
    }

    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsExpired(DateTime now) => now >= ExpiresAt;

    /// <summary>Lý do link không dùng được lúc <paramref name="now"/> (BR-09, BR-17); null nếu còn hiệu lực.</summary>
    public string? DenyReason(DateTime now) =>
        IsRevoked ? "Revoked" : IsExpired(now) ? "Expired" : null;

    /// <summary>Thu hồi trước hạn (Producer, hoặc tự động khi mở lại chương). Đã thu hồi thì giữ nguyên.</summary>
    public void Revoke(Guid? byUserId, string reason)
    {
        if (IsRevoked) return;
        RevokedAt = DateTime.UtcNow;
        RevokedByUserId = byUserId;
        RevokeReason = reason;
        UpdatedAt = RevokedAt;
    }
}
