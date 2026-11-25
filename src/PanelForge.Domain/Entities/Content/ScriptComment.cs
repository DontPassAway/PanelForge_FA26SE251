using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// D12: góp ý theo dòng trên kịch bản của một chương (Editor / Producer), không đổi trạng thái kịch bản và không
/// đi qua Assignment. Writer đọc rồi tự quyết định có "Hủy gửi duyệt" để sửa hay không; Writer đánh dấu đã xử lý.
/// </summary>
public class ScriptComment : BaseEntity
{
    public const int MaxBodyLength = 2000;

    public Guid ChapterId { get; private set; }
    public Guid? SceneId { get; private set; }
    /// <summary>Dòng được góp ý; null = góp ý chung cho cả cảnh / chương.</summary>
    public Guid? ScriptLineId { get; private set; }
    /// <summary>Nguyên văn đoạn kịch bản lúc góp ý (dòng có thể bị sửa sau đó).</summary>
    public string? QuotedText { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public Guid AuthorUserId { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public Guid? ResolvedById { get; private set; }

    private ScriptComment() { }

    public static ScriptComment Create(
        Guid chapterId, Guid? sceneId, Guid? scriptLineId, string? quotedText, string body, Guid authorUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        var trimmed = body.Trim();
        if (trimmed.Length > MaxBodyLength)
            throw new ArgumentException($"Góp ý tối đa {MaxBodyLength} ký tự.", nameof(body));

        return new ScriptComment
        {
            ChapterId = chapterId,
            SceneId = sceneId,
            ScriptLineId = scriptLineId,
            QuotedText = string.IsNullOrWhiteSpace(quotedText) ? null : quotedText.Trim(),
            Body = trimmed,
            AuthorUserId = authorUserId,
            CreatedBy = authorUserId.ToString()
        };
    }

    public bool IsResolved => ResolvedAt.HasValue;

    public void Resolve(Guid userId)
    {
        if (IsResolved) return;
        ResolvedAt = DateTime.UtcNow;
        ResolvedById = userId;
    }
}
