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
/// <param name="LastDecisionComment">Nhận xét của lần bị yêu cầu sửa gần nhất (chỉ trả ở GET lưới).</param>
/// <param name="AttemptCount">Số lần đã nộp (chỉ trả ở GET lưới).</param>
public sealed record StageTaskDto(
    Guid Id, Guid PipelineStageId, Guid? PageId, StageTaskStatus Status, Guid? LotId, Guid? AssigneeUserId,
    bool IsInputOutdated, string? OutdatedReason, Guid? ReviewerUserId, bool IsStartable, DateTime? StartedAt, uint RowVersion,
    string? LastDecisionComment = null, int AttemptCount = 0, string? ReopenReason = null,
    bool HasScriptImpact = false)
{
    public static StageTaskDto From(StageTask t, Guid? assigneeUserId, bool isStartable) => new(
        t.Id, t.PipelineStageId, t.PageId, t.Status, t.LotId, assigneeUserId,
        t.IsInputOutdated, t.OutdatedReason, t.ReviewerUserId, isStartable, t.StartedAt, t.RowVersion,
        ReopenReason: t.ReopenReason,
        HasScriptImpact: t.IsInputOutdated && t.OutdatedReason == StageTask.ScriptChangedReason);
}

public sealed record StageTaskLotDto(
    Guid Id, Guid PipelineStageId, Guid AssigneeUserId, WorkspaceRole AssignedRole, string? Brief, DateTime? DueDate,
    StageTaskLotStatus Status, bool IsOverdue, int TaskCount, int ApprovedCount, uint RowVersion,
    bool IsAutoAssigned = false, string? AutoAssignReason = null)
{
    public static StageTaskLotDto From(StageTaskLot l, IReadOnlyCollection<StageTask> tasks, DateTime nowUtc) => new(
        l.Id, l.PipelineStageId, l.AssigneeUserId, l.AssignedRole, l.Brief, l.DueDate, l.Status, l.IsOverdue(nowUtc),
        tasks.Count, tasks.Count(t => t.Status == StageTaskStatus.Approved), l.RowVersion, l.IsAutoAssigned, l.AutoAssignReason);
}

// ─── Requests ─────────────────────────────────────────────────────────────────

/// <param name="TaskIds">Các ô cùng công đoạn <paramref name="PipelineStageId"/>, đang Unassigned.</param>
public sealed record StageTaskLotRequest(
    Guid PipelineStageId, Guid AssigneeUserId, IReadOnlyList<Guid> TaskIds, string? Brief = null, DateTime? DueDate = null);

/// <param name="DryRun">true = chỉ kiểm tra, không lưu.</param>
/// <param name="Reassign">true = cho chuyển cả ô Assigned/InProgress/ReworkRequired (không lỗi thời) sang lô mới. Chỉ Producer.</param>
/// <param name="ConfirmStartedWork">Bắt buộc true khi đổi người ở ô InProgress/ReworkRequired (người đang làm bị gỡ khỏi ô).</param>
/// <param name="ExpectedRowVersions">taskId → rowVersion lúc người dùng xem; bắt buộc cho ô InProgress/ReworkRequired khi Reassign.</param>
public sealed record AssignStageTaskLotsRequest(IReadOnlyList<StageTaskLotRequest> Lots, bool DryRun = false, bool Reassign = false,
    bool ConfirmStartedWork = false, IReadOnlyDictionary<Guid, uint>? ExpectedRowVersions = null);

/// <summary>Sửa hạn / brief của lô (Producer). Trường null = giữ nguyên; ClearDueDate = true để bỏ hạn.</summary>
public sealed record UpdateStageTaskLotRequest(string? Brief, DateTime? DueDate, bool ClearDueDate = false, uint? ExpectedRowVersion = null);

public sealed record SubmitStageTasksRequest(IReadOnlyList<Guid> TaskIds);

/// <summary>Nhận xét bắt buộc (BR-06), lưu vào lần nộp bị từ chối và gửi người làm.</summary>
public sealed record RequestStageTaskChangesRequest(string Comment);

public sealed record StageTaskSubmissionDto(Guid Id, int AttemptNumber, string Status, Guid SubmittedByUserId, DateTime SubmittedAt,
    IReadOnlyList<Guid> ArtworkReferenceIds, Guid? DecidedByUserId, DateTime? DecidedAt, string? DecisionComment,
    string PanelLayoutJson = "[]", string CanvasVersionsJson = "{}", Guid? ScriptVersionId = null);

public sealed record UnassignStageTasksRequest(IReadOnlyList<Guid> TaskIds);

/// <summary>Reason bắt buộc; ghi vào OutdatedReason của các ô hạ nguồn bị gắn cờ.</summary>
public sealed record ReopenStageTaskRequest(string Reason);

/// <summary>Keep = giữ bài hiện tại; false = đưa ô về ReworkRequired.</summary>
public sealed record ResolveStageTaskImpactRequest(bool Keep);

public sealed record ReopenStageTaskResultDto(StageTaskDto Task, IReadOnlyList<Guid> OutdatedTaskIds);

/// <param name="Lots">Lô đã tạo; rỗng khi DryRun.</param>
public sealed record AssignStageTaskLotsResultDto(bool DryRun, int TaskCount, IReadOnlyList<StageTaskLotDto> Lots);

/// <summary>Phương án giao tự động, chưa lưu. Xác nhận bằng POST /lots với TaskIds và AssigneeUserId của từng lô.</summary>
/// <param name="UnassignedCount">Tổng số ô chưa giao của chương, gồm cả ô ở Skipped và ô của công đoạn đang tắt.</param>
public sealed record AutoAssignPlanDto(
    Guid ChapterId, int UnassignedCount, IReadOnlyList<AutoAssignLotDto> Lots, IReadOnlyList<AutoAssignSkippedDto> Skipped);

/// <param name="PageNumbers">Số trang của lô; rỗng với ô cấp Chương.</param>
/// <param name="OpenTaskCount">Số ô chưa xong người này đang giữ trong Workspace, trước phương án.</param>
/// <param name="WorkUnits">Khối lượng của lô: tổng số panel các trang (mỗi ô tối thiểu 1).</param>
/// <param name="OpenWorkUnits">Khối lượng việc chưa xong người này đang giữ trong Workspace, trước phương án.</param>
/// <param name="SuggestedDueDate">Hạn gợi ý (UTC) tính ngược từ ngày phát hành dự kiến của chương; null nếu chương chưa có ngày.</param>
public sealed record AutoAssignLotDto(
    Guid PipelineStageId, string StageName, Guid AssigneeUserId, string AssigneeName, WorkspaceRole AssignedRole,
    IReadOnlyList<Guid> TaskIds, IReadOnlyList<int> PageNumbers, int OpenTaskCount,
    int WorkUnits, int OpenWorkUnits, DateTime? SuggestedDueDate);

public sealed record AutoAssignSkippedDto(Guid PipelineStageId, string StageName, int TaskCount, string Reason);

public sealed record GenerateStageTasksResultDto(int Created, int Cancelled, int Total);

// ─── Clean Cutover ────────────────────────────────────────────────────────────

/// <param name="Migrated">Số việc V2 đã chuyển (mỗi việc thành một lô).</param>
/// <param name="CellsAssigned">Số ô được gắn vào các lô đó.</param>
/// <param name="Skipped">Việc giữ nguyên ở V2 kèm lý do.</param>
public sealed record MigrateAssignmentsResultDto(int Migrated, int CellsAssigned, IReadOnlyList<MigrateSkippedDto> Skipped);

public sealed record MigrateSkippedDto(Guid AssignmentId, Guid ChapterId, string Reason);

/// <summary>Một lô của người gọi, xuyên chương (thay cho my-assignments V2).</summary>
public sealed record MyTaskLotDto(
    Guid Id, Guid SeriesId, string SeriesTitle, Guid ChapterId, decimal ChapterNumber, string? ChapterTitle,
    Guid PipelineStageId, string StageName, int StageOrder, string? StageColorCode, WorkspaceRole AssignedRole,
    string? Brief, DateTime? DueDate, StageTaskLotStatus Status, bool IsOverdue, IReadOnlyList<MyTaskCellDto> Tasks);

/// <param name="PageNumber">null = ô cấp Chương.</param>
public sealed record MyTaskCellDto(
    Guid Id, Guid? PageId, int? PageNumber, StageTaskStatus Status, bool IsStartable, bool IsInputOutdated, string? OutdatedReason,
    string? LastDecisionComment = null, int AttemptCount = 0, string? ReopenReason = null);

/// <summary>Ô đang chờ duyệt (Submitted) hoặc đang được duyệt (UnderReview).</summary>
public sealed record TaskGridReviewItemDto(
    Guid TaskId, Guid SeriesId, string SeriesTitle, Guid ChapterId, decimal ChapterNumber, string? ChapterTitle,
    Guid PipelineStageId, string StageName, Guid? PageId, int? PageNumber, StageTaskStatus Status,
    Guid? AssigneeUserId, Guid? ReviewerUserId, int? AttemptNumber, DateTime? SubmittedAt,
    string? ReviewerName = null, bool IsInputOutdated = false);
