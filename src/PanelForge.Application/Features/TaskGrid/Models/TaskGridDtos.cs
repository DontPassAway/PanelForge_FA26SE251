using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Models;

public sealed record TaskGridDto(
    Guid ChapterId,
    IReadOnlyList<TaskGridStageDto> Stages,
    IReadOnlyList<TaskGridPageDto> Pages,
    IReadOnlyList<StageTaskDto> Tasks,
    IReadOnlyList<StageTaskLotDto> Lots);

/// <param name="PrerequisiteStageIds">Công đoạn tiền đề trực tiếp theo nhánh (BR-19).</param>
public sealed record TaskGridStageDto(
    Guid Id, string Name, int StageOrder, string? Track, WorkspaceRole? AllowedRole, bool IsApprovalGate,
    WorkflowEntityType Granularity, bool IsEnabled, IReadOnlyList<Guid> PrerequisiteStageIds);

public sealed record TaskGridPageDto(Guid Id, int PageNumber);

/// <param name="PageId">null = ô cấp Chương.</param>
/// <param name="IsStartable">Người nhận bấm Start được ngay (đã giao, tiền đề đã duyệt, không lỗi thời).</param>
/// <param name="RowVersion">Gửi lại khi sửa để chống ghi đè (xmin).</param>
public sealed record StageTaskDto(
    Guid Id, Guid PipelineStageId, Guid? PageId, StageTaskStatus Status, Guid? LotId, Guid? AssigneeUserId,
    bool IsInputOutdated, string? OutdatedReason, Guid? ReviewerUserId, bool IsStartable, DateTime? StartedAt, uint RowVersion)
{
    public static StageTaskDto From(StageTask t, Guid? assigneeUserId, bool isStartable) => new(
        t.Id, t.PipelineStageId, t.PageId, t.Status, t.LotId, assigneeUserId,
        t.IsInputOutdated, t.OutdatedReason, t.ReviewerUserId, isStartable, t.StartedAt, t.RowVersion);
}

public sealed record StageTaskLotDto(
    Guid Id, Guid PipelineStageId, Guid AssigneeUserId, WorkspaceRole AssignedRole, string? Brief, DateTime? DueDate,
    StageTaskLotStatus Status, bool IsOverdue, int TaskCount, int ApprovedCount, uint RowVersion)
{
    public static StageTaskLotDto From(StageTaskLot l, IReadOnlyCollection<StageTask> tasks, DateTime nowUtc) => new(
        l.Id, l.PipelineStageId, l.AssigneeUserId, l.AssignedRole, l.Brief, l.DueDate, l.Status, l.IsOverdue(nowUtc),
        tasks.Count, tasks.Count(t => t.Status == StageTaskStatus.Approved), l.RowVersion);
}

// ─── Requests ─────────────────────────────────────────────────────────────────

/// <param name="TaskIds">Các ô cùng công đoạn <paramref name="PipelineStageId"/>, đang Unassigned.</param>
public sealed record StageTaskLotRequest(
    Guid PipelineStageId, Guid AssigneeUserId, IReadOnlyList<Guid> TaskIds, string? Brief = null, DateTime? DueDate = null);

/// <param name="DryRun">true = chỉ kiểm tra, không lưu.</param>
public sealed record AssignStageTaskLotsRequest(IReadOnlyList<StageTaskLotRequest> Lots, bool DryRun = false);

public sealed record SubmitStageTasksRequest(IReadOnlyList<Guid> TaskIds);

/// <summary>Nhận xét bắt buộc (BR-06), lưu vào lần nộp bị từ chối và gửi người làm.</summary>
public sealed record RequestStageTaskChangesRequest(string Comment);

public sealed record StageTaskSubmissionDto(Guid Id, int AttemptNumber, string Status, Guid SubmittedByUserId, DateTime SubmittedAt,
    IReadOnlyList<Guid> ArtworkReferenceIds, Guid? DecidedByUserId, DateTime? DecidedAt, string? DecisionComment);

public sealed record UnassignStageTasksRequest(IReadOnlyList<Guid> TaskIds);

/// <summary>Reason bắt buộc; ghi vào OutdatedReason của các ô hạ nguồn bị gắn cờ.</summary>
public sealed record ReopenStageTaskRequest(string Reason);

/// <summary>Keep = giữ bài hiện tại; false = đưa ô về ReworkRequired.</summary>
public sealed record ResolveStageTaskImpactRequest(bool Keep);

public sealed record ReopenStageTaskResultDto(StageTaskDto Task, IReadOnlyList<Guid> OutdatedTaskIds);

/// <param name="Lots">Lô đã tạo; rỗng khi DryRun.</param>
public sealed record AssignStageTaskLotsResultDto(bool DryRun, int TaskCount, IReadOnlyList<StageTaskLotDto> Lots);

public sealed record GenerateStageTasksResultDto(int Created, int Cancelled, int Total);
