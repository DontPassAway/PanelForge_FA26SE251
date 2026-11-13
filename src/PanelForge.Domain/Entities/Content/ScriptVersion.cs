using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// "Commit" kịch bản cấp chương: ảnh chụp bất biến toàn bộ kịch bản (Trang → Panel → dòng) tại một thời điểm,
/// đánh số tăng dần theo chương. Tạo khi Writer gửi duyệt; nội dung trùng phiên bản trước thì dùng lại phiên bản đó.
/// Không phụ thuộc Scene (Scene đang được gỡ khỏi mô hình kịch bản).
/// </summary>
public class ScriptVersion : BaseEntity
{
    public const int MaxMessageLength = 500;

    public Guid ChapterId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public Guid AuthorUserId { get; private set; }
    /// <summary>SHA-256 của nội dung, để nhận ra lần gửi không đổi gì.</summary>
    public string ContentHash { get; private set; } = string.Empty;
    /// <summary>Các hàng kịch bản theo thứ tự đọc (JSON), xem ScriptVersionRow ở Application.</summary>
    public string RowsJson { get; private set; } = "[]";
    public int LineCount { get; private set; }
    /// <summary>Thời điểm phiên bản được gửi duyệt ("push"); null = mới lưu ("commit") chưa gửi.</summary>
    public DateTime? SubmittedAt { get; private set; }
    public Guid? SubmittedById { get; private set; }

    private ScriptVersion() { }

    /// <summary>Gửi duyệt = push phiên bản này (lưu = commit).</summary>
    public void MarkSubmitted(Guid userId)
    {
        SubmittedAt = DateTime.UtcNow;
        SubmittedById = userId;
    }

    public static ScriptVersion Create(
        Guid chapterId, int versionNumber, string? message, Guid authorUserId,
        string contentHash, string rowsJson, int lineCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);
        var text = string.IsNullOrWhiteSpace(message) ? $"Phiên bản {versionNumber}" : message.Trim();
        if (text.Length > MaxMessageLength) text = text[..MaxMessageLength];

        return new ScriptVersion
        {
            ChapterId = chapterId,
            VersionNumber = versionNumber,
            Message = text,
            AuthorUserId = authorUserId,
            ContentHash = contentHash,
            RowsJson = rowsJson,
            LineCount = lineCount,
            CreatedBy = authorUserId.ToString()
        };
    }
}
