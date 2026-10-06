using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Một lần nộp của một Assignment (CF3, design-research M7). Quyết định duyệt gắn vào đúng lần nộp này nên lịch sử
/// các lần nộp và lý do từ chối được giữ nguyên (BR-01). Chưa có <c>revisionRef</c>: artwork chưa có revision riêng.
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

    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? DecisionComment { get; private set; }

    private Submission() { }

    public static Submission Create(Guid assignmentId, Guid chapterId, Guid submittedByUserId, int attemptNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptNumber);

        return new Submission
        {
            AssignmentId = assignmentId,
            ChapterId = chapterId,
            SubmittedByUserId = submittedByUserId,
            AttemptNumber = attemptNumber,
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
