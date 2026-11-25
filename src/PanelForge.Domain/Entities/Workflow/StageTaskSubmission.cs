using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Một lần nộp của một ô lưới V3 (<see cref="StageTask"/>). Append-only (BR-01): quyết định duyệt / trả về gắn vào đúng lần nộp,
/// ảnh chụp các AssetReference mới nhất của ô lúc nộp. Bảng riêng với <see cref="Submission"/> cũ (gắn Assignment) cho tới Clean Cutover.
/// </summary>
public class StageTaskSubmission : BaseEntity
{
    public Guid StageTaskId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid SubmittedByUserId { get; private set; }
    /// <summary>Lần nộp thứ mấy của ô, bắt đầu từ 1.</summary>
    public int AttemptNumber { get; private set; }
    public SubmissionStatus Status { get; private set; }
    public DateTime SubmittedAt { get; private set; }
    /// <summary>AssetReference mới nhất của từng (Page, Panel) của ô, ở công đoạn của ô, lúc nộp.</summary>
    public List<Guid> ArtworkReferenceIds { get; private set; } = [];
    /// <summary>Bố cục Panel của các trang của ô lúc nộp (JSON như <see cref="Submission.PanelLayoutJson"/>). "[]" nếu không có Panel.</summary>
    public string PanelLayoutJson { get; private set; } = "[]";
    /// <summary>Version event stream canvas của từng trang của ô lúc nộp, <c>{"pageId": version}</c> (lớp chữ của Letterer).</summary>
    public string CanvasVersionsJson { get; private set; } = "{}";
    /// <summary>ScriptVersion (bản kịch bản chương đã commit) mới nhất lúc nộp; null nếu chương chưa commit kịch bản.</summary>
    public Guid? ScriptVersionId { get; private set; }

    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? DecisionComment { get; private set; }

    private StageTaskSubmission() { }

    /// <param name="autoApproved">Công đoạn không có cổng duyệt: lần nộp được duyệt ngay bởi chính người nộp.</param>
    public static StageTaskSubmission Create(Guid stageTaskId, Guid chapterId, Guid submittedByUserId, int attemptNumber,
        IEnumerable<Guid>? artworkReferenceIds, bool autoApproved,
        string? panelLayoutJson = null, string? canvasVersionsJson = null, Guid? scriptVersionId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptNumber);
        var now = DateTime.UtcNow;
        return new StageTaskSubmission
        {
            StageTaskId = stageTaskId,
            ChapterId = chapterId,
            SubmittedByUserId = submittedByUserId,
            AttemptNumber = attemptNumber,
            ArtworkReferenceIds = artworkReferenceIds?.Distinct().ToList() ?? [],
            PanelLayoutJson = string.IsNullOrWhiteSpace(panelLayoutJson) ? "[]" : panelLayoutJson,
            CanvasVersionsJson = string.IsNullOrWhiteSpace(canvasVersionsJson) ? "{}" : canvasVersionsJson,
            ScriptVersionId = scriptVersionId,
            Status = autoApproved ? SubmissionStatus.Approved : SubmissionStatus.Pending,
            DecidedByUserId = autoApproved ? submittedByUserId : null,
            DecidedAt = autoApproved ? now : null,
            SubmittedAt = now,
            CreatedAt = now
        };
    }

    public void Approve(Guid decidedByUserId) => Decide(SubmissionStatus.Approved, decidedByUserId, null);

    /// <summary>Trả về bắt buộc có comment (BR-06).</summary>
    public void Reject(Guid decidedByUserId, string comment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        Decide(SubmissionStatus.Rejected, decidedByUserId, comment.Trim());
    }

    /// <summary>Lần nộp còn chờ nhưng ô đã bị rút bài / mở lại / làm lại: hết hiệu lực.</summary>
    public void Supersede()
    {
        RequirePending(SubmissionStatus.Superseded);
        Status = SubmissionStatus.Superseded;
        UpdatedAt = DateTime.UtcNow;
    }

    private void Decide(SubmissionStatus status, Guid by, string? comment)
    {
        RequirePending(status);
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
