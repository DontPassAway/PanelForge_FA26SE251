using System.Security.Cryptography;
using System.Text;
using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Bible;

/// <summary>
/// Sự kiện duyệt append-only của một phiên bản Bible: nộp Concept, yêu cầu sửa, duyệt, khoá, bị thay thế.
/// Phiên bản (nội dung + ContentHash) không đổi; mọi bước duyệt chỉ thêm dòng ở đây, không sửa/xoá dòng cũ (BR-01).
/// EventHash băm toàn bộ payload để phát hiện sửa tay; kiểm lại bằng <see cref="HasValidHash"/>.
/// <para>
/// Lưu ý migration: các migration sau đã sửa trường được băm của dòng CŨ nên việc kiểm lại dòng cũ được kỳ vọng là sai:
/// <list type="bullet">
/// <item><description>20261116082910_ConvertEndorsedBibleRevisionEvents: đổi event_type.</description></item>
/// <item><description>20261128063022_BackfillBibleConceptEventPointers: điền concept_version_number cho dữ liệu trước 2026-11-14.</description></item>
/// <item><description>20261126175800_VariantScopedBibleVersioning: đánh số lại version_number (comment "Bị thay bởi vN" cũ trỏ sai số).</description></item>
/// </list>
/// Ngoài ra, trước thay đổi này CreatedAt bị SaveChangesAsync ghi đè bằng thời điểm lưu, nên hash cũ không khớp thời điểm
/// đã băm; dòng cũ không kiểm lại được. Không băm lại, không sửa dòng cũ.
/// </para>
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
        // Cắt về micro-giây: Postgres timestamptz chỉ giữ micro-giây, nên thời điểm băm phải đúng bằng thời điểm được lưu.
        var at = TruncateToMicroseconds(DateTime.UtcNow);
        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        imageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        assetSha256 = string.IsNullOrWhiteSpace(assetSha256) ? null : assetSha256.Trim().ToLowerInvariant();
        return new BibleRevisionEvent
        {
            BibleEntryRevisionId = revisionId,
            Type = type,
            ActorId = actorId,
            Comment = comment,
            ImageUrl = imageUrl,
            ConceptVersionNumber = conceptVersionNumber,
            AssetSha256 = assetSha256,
            EventHash = ComputeHash(revisionId, type, actorId, comment, imageUrl, conceptVersionNumber, assetSha256, at),
            CreatedAt = at
        };
    }

    /// <summary>Tính lại hash từ các trường đang có trên entity và so với EventHash đã lưu.</summary>
    public bool HasValidHash() =>
        ComputeHash(BibleEntryRevisionId, Type, ActorId, Comment, ImageUrl, ConceptVersionNumber, AssetSha256, CreatedAt)
        == EventHash;

    /// <summary>Payload băm: revisionId|type|actorId|comment|imageUrl|conceptVersionNumber|assetSha256|createdAt (ISO 8601 'O').</summary>
    private static string ComputeHash(
        Guid revisionId,
        BibleRevisionEventType type,
        Guid? actorId,
        string? comment,
        string? imageUrl,
        int? conceptVersionNumber,
        string? assetSha256,
        DateTime createdAt)
    {
        var at = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc);
        var payload = $"{revisionId}|{type}|{actorId}|{comment}|{imageUrl}|{conceptVersionNumber}|{assetSha256}|{at:O}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, value.Kind);
}
