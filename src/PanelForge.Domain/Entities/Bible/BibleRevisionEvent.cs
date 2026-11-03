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
    public string EventHash { get; private set; } = default!;

    private BibleRevisionEvent() { }

    internal static BibleRevisionEvent Create(
        Guid revisionId, BibleRevisionEventType type, Guid? actorId, string? comment = null, string? imageUrl = null)
    {
        var at = DateTime.UtcNow;
        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        imageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        var payload = $"{revisionId}|{type}|{actorId}|{comment}|{imageUrl}|{at:O}";
        return new BibleRevisionEvent
        {
            BibleEntryRevisionId = revisionId,
            Type = type,
            ActorId = actorId,
            Comment = comment,
            ImageUrl = imageUrl,
            EventHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            CreatedAt = at
        };
    }
}
