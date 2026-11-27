namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// SCR-42 (D9c): Working copy khả biến cho autosave kịch bản của Writer.
/// Tự động dọn/đồng bộ khi Writer thực hiện Manual Save hoặc Submit.
/// </summary>
public class ChapterScriptWorkingCopy
{
    public Guid ChapterId { get; private set; }
    public Guid WriterUserId { get; private set; }
    public Guid BaseVersionId { get; private set; }
    public string ContentJson { get; private set; } = "{}";
    public string ContentHash { get; private set; } = string.Empty;
    public int LineCount { get; private set; }
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private ChapterScriptWorkingCopy() { }

    public static ChapterScriptWorkingCopy Create(
        Guid chapterId,
        Guid writerUserId,
        Guid baseVersionId,
        string contentJson,
        string contentHash,
        int lineCount)
    {
        if (chapterId == Guid.Empty) throw new ArgumentException("ChapterId không hợp lệ.", nameof(chapterId));
        if (writerUserId == Guid.Empty) throw new ArgumentException("WriterUserId không hợp lệ.", nameof(writerUserId));
        if (baseVersionId == Guid.Empty) throw new ArgumentException("BaseVersionId không hợp lệ.", nameof(baseVersionId));

        return new ChapterScriptWorkingCopy
        {
            ChapterId = chapterId,
            WriterUserId = writerUserId,
            BaseVersionId = baseVersionId,
            ContentJson = string.IsNullOrWhiteSpace(contentJson) ? "{}" : contentJson,
            ContentHash = contentHash ?? string.Empty,
            LineCount = Math.Max(0, lineCount),
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(Guid writerUserId, Guid baseVersionId, string contentJson, string contentHash, int lineCount)
    {
        WriterUserId = writerUserId;
        BaseVersionId = baseVersionId;
        ContentJson = string.IsNullOrWhiteSpace(contentJson) ? "{}" : contentJson;
        ContentHash = contentHash ?? string.Empty;
        LineCount = Math.Max(0, lineCount);
        UpdatedAt = DateTime.UtcNow;
    }
}
