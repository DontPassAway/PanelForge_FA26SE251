using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// MODEL CŨ (legacy, chưa dùng): mô hình 1 Chapter : 1 Script có Version/IsLocked mutable. Kịch bản thực tế hiện nằm ở
/// <see cref="ScriptVersion"/> (commit bất biến) + ScriptLine, trạng thái khoá ở Chapter.ScriptStatus / IsScriptFrozen.
/// Không có nơi nào tạo hay đọc entity này ngoài cấu hình EF; giữ lại vì bảng cần migration và quyết định của team để xoá.
/// </summary>
public class ChapterScript : BaseEntity
{
    public Guid ChapterId { get; private set; }
    public int Version { get; private set; } = 1;
    public string RawText { get; private set; } = string.Empty;
    public bool IsLocked { get; private set; } = false;
    public DateTime? LockedAt { get; private set; }
    public Guid? LockedById { get; private set; }

    public Chapter Chapter { get; private set; } = default!;

    private ChapterScript() { }

    public static ChapterScript Create(Guid chapterId, string? initialRawText = null)
    {
        if (chapterId == Guid.Empty)
            throw new ArgumentException("ChapterId must not be empty.", nameof(chapterId));

        return new ChapterScript
        {
            ChapterId = chapterId,
            Version = 1,
            RawText = initialRawText?.Trim() ?? string.Empty,
            IsLocked = false
        };
    }

    /// <summary>
    /// Updates the script raw text. Fails if the script is locked.
    /// </summary>
    public void UpdateRawText(string newRawText, Guid editorUserId)
    {
        if (IsLocked)
            throw new InvalidOperationException("Cannot edit script: the script is locked and frozen for production.");

        ArgumentNullException.ThrowIfNull(newRawText);

        if (RawText == newRawText)
            return;

        RawText = newRawText;
        Version++;
    }

    /// <summary>
    /// Locks the script to freeze narrative baseline for storyboarding / drawing.
    /// </summary>
    public void Lock(Guid producerUserId)
    {
        if (IsLocked)
            return;

        IsLocked = true;
        LockedAt = DateTime.UtcNow;
        LockedById = producerUserId;
    }

    /// <summary>
    /// Unlocks the script (only via explicit Producer action or approved reopen request).
    /// </summary>
    public void Unlock(Guid producerUserId)
    {
        if (!IsLocked)
            return;

        IsLocked = false;
        LockedAt = null;
        LockedById = null;
        Version++;
    }
}
