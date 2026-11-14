using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Ô việc của lưới giao việc V3: một Trang × một Công đoạn (<see cref="PageId"/> null = công đoạn cấp Chương).
/// Mỗi ô tối đa một người qua lô <see cref="LotId"/> (<see cref="StageTaskLot"/>). Cờ <see cref="IsInputOutdated"/> bật khi công đoạn tiền đề
/// bị mở lại sau khi ô đã bắt đầu; khi cờ bật thì không Start/Submit/Approve được cho tới khi xử lý Impact Review.
/// </summary>
public class StageTask : BaseEntity
{
    public const int MaxOutdatedReasonLength = 500;

    public Guid SeriesId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid PipelineStageId { get; private set; }
    public Guid? PageId { get; private set; }
    public Guid? LotId { get; private set; }
    public StageTaskStatus Status { get; private set; } = StageTaskStatus.Unassigned;
    public bool IsInputOutdated { get; private set; }
    public string? OutdatedReason { get; private set; }
    public Guid? ReviewerUserId { get; private set; }
    public Guid? CurrentSubmissionId { get; private set; }
    public DateTime? StartedAt { get; private set; }

    /// <summary>Concurrency token ánh xạ cột hệ thống <c>xmin</c> của PostgreSQL.</summary>
    public uint RowVersion { get; private set; }

    private StageTask() { }

    public static StageTask Create(Guid seriesId, Guid chapterId, Guid pipelineStageId, Guid? pageId) => new()
    {
        SeriesId = seriesId,
        ChapterId = chapterId,
        PipelineStageId = pipelineStageId,
        PageId = pageId,
        CreatedAt = DateTime.UtcNow
    };

    private bool HasStarted => Status is StageTaskStatus.InProgress or StageTaskStatus.Submitted
        or StageTaskStatus.UnderReview or StageTaskStatus.Approved or StageTaskStatus.ReworkRequired;

    /// <summary>Giao vào lô (lần đầu hoặc Transfer sang lô của người khác). Người mới chưa bắt đầu nên ô về Assigned.</summary>
    public void AssignTo(Guid lotId)
    {
        if (lotId == Guid.Empty) throw new ArgumentException("Thiếu lô giao việc.", nameof(lotId));
        if (Status is not (StageTaskStatus.Unassigned or StageTaskStatus.Assigned
            or StageTaskStatus.InProgress or StageTaskStatus.ReworkRequired))
            throw Invalid("giao việc");

        LotId = lotId;
        Status = StageTaskStatus.Assigned;
        ClearOutdated();
        Touch();
    }

    public void Unassign()
    {
        if (Status is not (StageTaskStatus.Assigned or StageTaskStatus.InProgress or StageTaskStatus.ReworkRequired))
            throw Invalid("bỏ gán");

        LotId = null;
        Status = StageTaskStatus.Unassigned;
        ClearOutdated();
        Touch();
    }

    /// <param name="prerequisitesApproved">Mọi ô tiền đề trực tiếp đã Approved và không mang cờ lỗi thời.</param>
    public void Start(bool prerequisitesApproved)
    {
        if (Status is not (StageTaskStatus.Assigned or StageTaskStatus.ReworkRequired)) throw Invalid("bắt đầu");
        if (!prerequisitesApproved) throw new InvalidOperationException("Công đoạn trước của trang này chưa được duyệt.");
        EnsureInputCurrent();

        Status = StageTaskStatus.InProgress;
        StartedAt ??= DateTime.UtcNow;
        Touch();
    }

    /// <param name="submissionId">Lần nộp lưu lịch sử; null khi chưa nối bảng Submission (lát cắt sau).</param>
    /// <param name="isApprovalGate">Công đoạn có cổng duyệt; không có thì nộp là tự duyệt.</param>
    public void Submit(Guid? submissionId, bool isApprovalGate)
    {
        if (Status != StageTaskStatus.InProgress) throw Invalid("nộp");
        EnsureInputCurrent();

        CurrentSubmissionId = submissionId;
        Status = isApprovalGate ? StageTaskStatus.Submitted : StageTaskStatus.Approved;
        Touch();
    }

    public void Withdraw()
    {
        if (Status != StageTaskStatus.Submitted) throw Invalid("rút bài");
        Status = StageTaskStatus.InProgress;
        Touch();
    }

    public void OpenReview(Guid reviewerUserId)
    {
        if (Status != StageTaskStatus.Submitted) throw Invalid("mở duyệt");
        ReviewerUserId = reviewerUserId;
        Status = StageTaskStatus.UnderReview;
        Touch();
    }

    public void ReleaseReview()
    {
        if (Status != StageTaskStatus.UnderReview) throw Invalid("trả về hàng đợi duyệt");
        ReviewerUserId = null;
        Status = StageTaskStatus.Submitted;
        Touch();
    }

    public void Approve()
    {
        if (Status != StageTaskStatus.UnderReview) throw Invalid("duyệt");
        EnsureInputCurrent();
        Status = StageTaskStatus.Approved;
        Touch();
    }

    /// <summary>
    /// Editor yêu cầu sửa. Nếu tiền đề đã duyệt lại thì gỡ cờ lỗi thời luôn, để người làm Start lại được
    /// (nếu không, ô kẹt ở ReworkRequired mãi vì Start đòi cờ tắt).
    /// </summary>
    public void RequestChanges(bool prerequisitesApproved)
    {
        if (Status != StageTaskStatus.UnderReview) throw Invalid("yêu cầu sửa");
        Status = StageTaskStatus.ReworkRequired;
        if (prerequisitesApproved) ClearOutdated();
        Touch();
    }

    /// <summary>Mở lại ô đã duyệt (thay cho ReturnWork). Không cho mở khi đầu vào đang lỗi thời: phải qua Impact Review.</summary>
    public void Reopen()
    {
        if (Status != StageTaskStatus.Approved) throw Invalid("mở lại");
        EnsureInputCurrent();
        Status = StageTaskStatus.ReworkRequired;
        Touch();
    }

    /// <summary>Công đoạn tiền đề bị mở lại. Chỉ gắn cờ cho ô đã bắt đầu; ô chưa bắt đầu vẫn bị chặn Start bởi tiền đề.</summary>
    public void MarkInputOutdated(string reason)
    {
        if (!HasStarted) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var trimmed = reason.Trim();
        IsInputOutdated = true;
        OutdatedReason = trimmed.Length <= MaxOutdatedReasonLength ? trimmed : trimmed[..MaxOutdatedReasonLength];
        Touch();
    }

    /// <summary>
    /// Impact Review: Keep gỡ cờ và giữ nguyên trạng thái; Rework đưa ô về ReworkRequired và gỡ cờ.
    /// Chỉ chạy khi mọi tiền đề trực tiếp đã duyệt lại, nếu không cờ sẽ sai ngay khi tiền đề nộp lại.
    /// Người gọi lo từ chối Submission đang chờ và tạo Change Request khi Rework.
    /// </summary>
    public void ResolveImpact(bool keep, bool prerequisitesApproved)
    {
        if (!IsInputOutdated) throw new InvalidOperationException("Ô này không mang cờ đầu vào lỗi thời.");
        if (!HasStarted) throw Invalid("xử lý Impact Review");
        if (!prerequisitesApproved)
            throw new InvalidOperationException("Công đoạn trước chưa được duyệt lại, chưa xử lý Impact Review được.");

        if (!keep) Status = StageTaskStatus.ReworkRequired;
        ClearOutdated();
        Touch();
    }

    /// <summary>Trang bị xoá hoặc bỏ khỏi chương sau khi mở lại bước chốt trang.</summary>
    public void Cancel()
    {
        if (Status == StageTaskStatus.Cancelled) return;
        Status = StageTaskStatus.Cancelled;
        ClearOutdated();
        Touch();
    }

    /// <summary>Công đoạn được bật lại (hoặc trang được khôi phục): ô đã huỷ trở về chưa giao, không mang theo lô cũ.</summary>
    public void Revive()
    {
        if (Status != StageTaskStatus.Cancelled) throw Invalid("dùng lại");
        LotId = null;
        ReviewerUserId = null;
        CurrentSubmissionId = null;
        Status = StageTaskStatus.Unassigned;
        Touch();
    }

    private void EnsureInputCurrent()
    {
        if (IsInputOutdated)
            throw new InvalidOperationException("Đầu vào của ô này đã lỗi thời, cần xử lý Impact Review trước.");
    }

    private void ClearOutdated()
    {
        IsInputOutdated = false;
        OutdatedReason = null;
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;

    private InvalidOperationException Invalid(string action) =>
        new($"Không thể {action} ô việc đang ở trạng thái {Status}.");
}
