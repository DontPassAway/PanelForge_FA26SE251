using System.Security.Cryptography;
using System.Text;
using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Bible;

/// <summary>
/// Sự kiện duyệt append-only của một phiên bản Bible: nộp Concept, yêu cầu sửa, duyệt, khoá, bị thay thế.
/// Phiên bản (nội dung + ContentHash) không đổi; mọi bước duyệt chỉ thêm dòng ở đây, không sửa/xoá dòng cũ (BR-01).
/// EventHash băm toàn bộ payload để phát hiện sửa tay.
/// </summary>
public class BibleRevisionEvent : BaseEntity
{
    public Guid BibleEntryRevisionId { get; private set; }
    public BibleRevisionEventType Type { get; private set; }
    public Guid? ActorId { get; private set; }
    public string? Comment { get; private set; }
    /// <summary>Ảnh Concept của sự kiện (ConceptSubmitted: ảnh vừa nộp; Locked: ảnh được chốt làm canon).</summary>
    public string? ImageUrl { get; private set; }
    /// <summary>V2.6 (Q20): Số thứ tự phiên bản Concept tăng dần trong revision (1, 2, ...).</summary>
    public int? ConceptVersionNumber { get; private set; }
    /// <summary>V2.6 (Q20): Mã băm CAS SHA-256 của ảnh concept asset.</summary>
    public string? AssetSha256 { get; private set; }
    public string EventHash { get; private set; } = default!;

    private BibleRevisionEvent() { }

    internal static BibleRevisionEvent Create(
        Guid revisionId,
        BibleRevisionEventType type,
        Guid? actorId,
        string? comment = null,
        string? imageUrl = null,
        int? conceptVersionNumber = null,
        string? assetSha256 = null)
    {
        var at = DateTime.UtcNow;
        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        imageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        assetSha256 = string.IsNullOrWhiteSpace(assetSha256) ? null : assetSha256.Trim().ToLowerInvariant();
        var payload = $"{revisionId}|{type}|{actorId}|{comment}|{imageUrl}|{conceptVersionNumber}|{assetSha256}|{at:O}";
        return new BibleRevisionEvent
        {
            BibleEntryRevisionId = revisionId,
            Type = type,
            ActorId = actorId,
            Comment = comment,
            ImageUrl = imageUrl,
            ConceptVersionNumber = conceptVersionNumber,
            AssetSha256 = assetSha256,
            EventHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            CreatedAt = at
        };
    }
}
