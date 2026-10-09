using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Revision nội dung (script / layout) của một Scene. Append-only (BR-01, BR-08): mỗi lần áp dụng thêm một dòng mới,
/// không bao giờ sửa/xoá. Mở rộng tối thiểu cho demo AI vì trước đây chỉ Bible có revision.
/// VersionNumber tăng dần theo (SceneId, Kind); revision hiệu lực = số lớn nhất.
/// </summary>
public class ContentRevision : BaseEntity
{
    public Guid ChapterId { get; private set; }
    public Guid SceneId { get; private set; }
    public ContentRevisionKind Kind { get; private set; }
    public int VersionNumber { get; private set; }
    public string Summary { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public string ContentHash { get; private set; } = default!;
    public Guid? PreviousRevisionId { get; private set; }
    /// <summary>Đề xuất AI sinh ra revision này; null với revision baseline chụp lại nội dung có sẵn.</summary>
    public Guid? SourceSuggestionId { get; private set; }
    /// <summary>Ánh xạ payload → bản ghi thật được tạo (ví dụ panelNumber → panelId) để truy ngược.</summary>
    public string MappingJson { get; private set; } = "{}";

    private ContentRevision() { }

    public static ContentRevision Create(
        Guid chapterId, Guid sceneId, ContentRevisionKind kind, int versionNumber, string summary,
        string payloadJson, string contentHash, Guid? previousRevisionId, Guid? sourceSuggestionId,
        string? mappingJson = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        return new ContentRevision
        {
            ChapterId = chapterId,
            SceneId = sceneId,
            Kind = kind,
            VersionNumber = versionNumber,
            Summary = summary,
            PayloadJson = payloadJson,
            ContentHash = contentHash,
            PreviousRevisionId = previousRevisionId,
            SourceSuggestionId = sourceSuggestionId,
            MappingJson = string.IsNullOrWhiteSpace(mappingJson) ? "{}" : mappingJson
        };
    }
}