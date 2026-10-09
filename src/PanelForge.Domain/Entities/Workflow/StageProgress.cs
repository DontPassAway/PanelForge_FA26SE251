using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Nguồn sự thật duy nhất của tiến độ: một Panel ở một Stage. <c>Page.CurrentStageId</c> và
/// <c>Panel.CurrentStageId</c> chỉ là giá trị suy ra từ các dòng này.
/// </summary>
public class StageProgress : BaseEntity
{
    public Guid PanelId { get; private set; }
    public Guid PipelineStageId { get; private set; }
    public StageProgressStatus Status { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public Guid? SubmittedByUserId { get; private set; }

    private StageProgress() { }

    /// <summary>NotStarted → InProgress (điều kiện stage trước đã Approved do handler kiểm tra, BR-19).</summary>
    public static StageProgress Begin(Guid panelId, Guid pipelineStageId) => new()
    {
        PanelId = panelId,
        PipelineStageId = pipelineStageId,
        Status = StageProgressStatus.InProgress,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    /// <summary>Bắt đầu lại: chỉ từ InProgress (idempotent) hoặc ChangesRequested.</summary>
    public void Resume()
    {
        if (Status == StageProgressStatus.InProgress) return;
        if (Status != StageProgressStatus.ChangesRequested)
            throw new InvalidOperationException($"Panel đang ở trạng thái {Status} ở stage này, không thể bắt đầu lại.");

        Status = StageProgressStatus.InProgress;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Submitted → InReview (Editor mở).</summary>
    public void OpenReview() => Move(StageProgressStatus.Submitted, StageProgressStatus.InReview);

    /// <summary>InReview → Approved. Việc người duyệt khác người nộp do handler kiểm tra.</summary>
    public void Approve() => Move(StageProgressStatus.InReview, StageProgressStatus.Approved);

    /// <summary>InReview → ChangesRequested (comment bắt buộc do handler kiểm tra).</summary>
    public void RequestChanges() => Move(StageProgressStatus.InReview, StageProgressStatus.ChangesRequested);

    private void Move(StageProgressStatus from, StageProgressStatus to)
    {
        if (Status != from)
            throw new InvalidOperationException($"Panel đang ở trạng thái {Status} ở stage này, cần {from} để chuyển sang {to}.");

        Status = to;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>InProgress → Submitted.</summary>
    public void Submit(Guid userId)
    {
        if (Status != StageProgressStatus.InProgress)
            throw new InvalidOperationException($"Panel đang ở trạng thái {Status} ở stage này, không thể nộp.");

        Status = StageProgressStatus.Submitted;
        SubmittedAt = DateTime.UtcNow;
        SubmittedByUserId = userId;
        UpdatedAt = DateTime.UtcNow;
    }
}
