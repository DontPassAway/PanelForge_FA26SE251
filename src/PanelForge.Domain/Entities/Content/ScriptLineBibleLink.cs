using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// UC-06 & V2.5: Dòng script nhắc tới một mục Series Bible (nhân vật, địa điểm, đạo cụ, thuật ngữ...), ngoài speaker.
/// Hỗ trợ Dynamic Entity Linking (@-mention), Stub Entity auto-matching và Era Version Resolution.
/// Bỏ liên kết = xoá mềm (giữ lịch sử); mỗi cặp (dòng, mục) chỉ có một liên kết đang hoạt động.
/// </summary>
public class ScriptLineBibleLink : BaseEntity
{
    public const int MaxPerLine = 20;

    public Guid ScriptLineId { get; private set; }
    public Guid BibleEntryId { get; private set; }
    /// <summary>V2.5: Chuỗi text thô lúc Writer gõ @-mention trong kịch bản.</summary>
    public string? RawMentionText { get; private set; }
    /// <summary>V2.5: Phiên bản BibleEntryRevision có hiệu lực tại thời điểm bind hoặc tại Chapter của kịch bản.</summary>
    public Guid? ResolvedRevisionId { get; private set; }

    public ScriptLine ScriptLine { get; private set; } = default!;
    public BibleEntry BibleEntry { get; private set; } = default!;
    public BibleEntryRevision? ResolvedRevision { get; private set; }

    private ScriptLineBibleLink() { }

    public static ScriptLineBibleLink Create(
        Guid scriptLineId,
        Guid bibleEntryId,
        string? rawMentionText = null,
        Guid? resolvedRevisionId = null) =>
        new()
        {
            ScriptLineId = scriptLineId,
            BibleEntryId = bibleEntryId,
            RawMentionText = rawMentionText?.Trim(),
            ResolvedRevisionId = resolvedRevisionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    public void UpdateResolvedRevision(Guid? revisionId)
    {
        ResolvedRevisionId = revisionId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RemapTargetEntry(Guid newBibleEntryId, Guid? newResolvedRevisionId = null)
    {
        BibleEntryId = newBibleEntryId;
        ResolvedRevisionId = newResolvedRevisionId;
        UpdatedAt = DateTime.UtcNow;
    }
}
