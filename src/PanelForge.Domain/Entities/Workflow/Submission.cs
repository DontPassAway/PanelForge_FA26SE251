using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Một lần nộp của một Assignment (CF3, design-research M7). Quyết định duyệt gắn vào đúng lần nộp này nên lịch sử
/// các lần nộp và lý do từ chối được giữ nguyên (BR-01). Gắn đúng bản được nộp: các phiên bản artwork mới nhất lúc nộp
/// (<see cref="ArtworkReferenceIds"/>) và version của Chapter (<see cref="ChapterVersion"/>, cho việc Script).
/// </summary>
public class Submission : BaseEntity
{
    public Guid AssignmentId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid SubmittedByUserId { get; private set; }
    /// <summary>Lần nộp thứ mấy của Assignment, bắt đầu từ 1.</summary>
    public int AttemptNumber { get; private set; }
    public SubmissionStatus Status { get; private set; }
    public DateTime SubmittedAt { get; private set; }
    /// <summary>AssetReference mới nhất của từng (Page, Panel) mà việc bao phủ, ở stage của việc, tại lúc nộp. Rỗng với việc không có artwork.</summary>
    public List<Guid> ArtworkReferenceIds { get; private set; } = [];
    /// <summary>Chapter.VersionVector lúc nộp (revision của script).</summary>
    public long ChapterVersion { get; private set; }
    /// <summary>
    /// Bố cục Panel của các trang việc bao phủ tại lúc nộp (mảng JSON: id, pageId, panelNumber, readingOrder, boundingBox).
    /// Lịch sử lần nộp phải dựng lại được dù sau đó Panel bị xoá hay đổi toạ độ (BR-01). "[]" với việc không có Panel
    /// và với các lần nộp có trước khi thêm cột này.
    /// </summary>
    public string PanelLayoutJson { get; private set; } = "[]";
    /// <summary>
    /// Version event stream (Marten) của từng trang việc bao phủ lúc nộp, dạng JSON <c>{"pageId": version}</c>.
    /// Việc Letter không có artwork: lớp chữ được dựng lại đúng tại version này để duyệt (BR-01). "{}" với lần nộp cũ.
    /// </summary>
    public string CanvasVersionsJson { get; private set; } = "{}";
    /// <summary>
    /// ContentRevision (Script, append-only) mới nhất của từng Scene trong chương lúc nộp: bản script mà kết quả này viết ra
    /// (Writer) hoặc dựa vào (Artist, Letterer). Không đổi theo script "latest" về sau. Rỗng với lần nộp cũ.
    /// </summary>
    public List<Guid> ScriptRevisionIds { get; private set; } = [];

    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? DecisionComment { get; private set; }

    private Submission() { }

    public static Submission Create(
        Guid assignmentId, Guid chapterId, Guid submittedByUserId, int attemptNumber,
        IEnumerable<Guid>? artworkReferenceIds = null, long chapterVersion = 0, string? panelLayoutJson = null,
        string? canvasVersionsJson = null, IEnumerable<Guid>? scriptRevisionIds = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptNumber);

        return new Submission
        {
            AssignmentId = assignmentId,
            ChapterId = chapterId,
            SubmittedByUserId = submittedByUserId,
            AttemptNumber = attemptNumber,
            ArtworkReferenceIds = artworkReferenceIds?.Distinct().ToList() ?? [],
            ChapterVersion = chapterVersion,
            PanelLayoutJson = string.IsNullOrWhiteSpace(panelLayoutJson) ? "[]" : panelLayoutJson,
            CanvasVersionsJson = string.IsNullOrWhiteSpace(canvasVersionsJson) ? "{}" : canvasVersionsJson,
            ScriptRevisionIds = scriptRevisionIds?.Distinct().ToList() ?? [],
            Status = SubmissionStatus.Pending,
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Approve(Guid decidedByUserId)
    {
        RequirePending(SubmissionStatus.Approved);
        Decide(SubmissionStatus.Approved, decidedByUserId, null);
    }

    /// <summary>Trả về bắt buộc có comment (BR-06).</summary>
    public void Reject(Guid decidedByUserId, string comment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        RequirePending(SubmissionStatus.Rejected);
        Decide(SubmissionStatus.Rejected, decidedByUserId, comment.Trim());
    }

    /// <summary>Nộp lại làm bản còn Pending trước đó thành Superseded.</summary>
    public void Supersede()
    {
        RequirePending(SubmissionStatus.Superseded);
        Status = SubmissionStatus.Superseded;
        UpdatedAt = DateTime.UtcNow;
    }

    private void Decide(SubmissionStatus status, Guid by, string? comment)
    {
        Status = status;
        DecidedByUserId = by;
        DecidedAt = DateTime.UtcNow;
        DecisionComment = comment;
        UpdatedAt = DateTime.UtcNow;
    }

    private void RequirePending(SubmissionStatus target)
    {
        if (Status != SubmissionStatus.Pending)
            throw new InvalidOperationException($"Lần nộp đang ở trạng thái {Status}, không thể chuyển sang {target}.");
    }
}
