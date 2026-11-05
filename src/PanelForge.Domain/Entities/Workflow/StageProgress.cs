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
    public bool HasScriptImpact { get; private set; } = false;
    public Guid? ImpactedScriptLineId { get; private set; }

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

    /// <summary>Tự động duyệt cho stage không yêu cầu Editor duyệt (Internal Handoff Pencil ➔ Ink...).</summary>
    public void AutoApprove(Guid userId)
    {
        if (Status == StageProgressStatus.Approved) return;
        SubmittedAt = DateTime.UtcNow;
        SubmittedByUserId = userId;
        Status = StageProgressStatus.Approved;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>InReview → ChangesRequested (comment bắt buộc do handler kiểm tra).</summary>
    public void RequestChanges() => Move(StageProgressStatus.InReview, StageProgressStatus.ChangesRequested);

    /// <summary>CF5 bước 7: chương mở lại, phản hồi xem thử thành yêu cầu sửa → stage đã duyệt phải làm lại (Approved → ChangesRequested).</summary>
    public void ReopenForRework() => Move(StageProgressStatus.Approved, StageProgressStatus.ChangesRequested);

    /// <summary>V2.5 & WF04: Đánh cờ tác động do kịch bản đổi và chuyển Approved về ChangesRequested.</summary>
    public void FlagScriptImpact(Guid scriptLineId)
    {
        HasScriptImpact = true;
        ImpactedScriptLineId = scriptLineId;
        if (Status == StageProgressStatus.Approved || Status == StageProgressStatus.InReview || Status == StageProgressStatus.Submitted)
        {
            Status = StageProgressStatus.ChangesRequested;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>V2.5 & WF04: Gỡ cờ tác động kịch bản.</summary>
    public void ClearScriptImpact()
    {
        HasScriptImpact = false;
        ImpactedScriptLineId = null;
        UpdatedAt = DateTime.UtcNow;
    }

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
