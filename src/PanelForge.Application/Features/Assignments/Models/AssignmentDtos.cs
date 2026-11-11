using System.Text.Json;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Models;

public sealed record AssignmentDto(
    Guid Id,
    Guid SeriesId,
    Guid ChapterId,
    WorkflowEntityType EntityType,
    Guid TargetEntityId,
    Guid PipelineStageId,
    Guid AssigneeUserId,
    WorkspaceRole AssignedRole,
    string Brief,
    string? ReferenceNotes,
    DateTime? DueDate,
    AssignmentPriority Priority,
    AssignmentStatus Status,
    Guid? DependsOnAssignmentId,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? LastFeedbackComment,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<Guid>? DependsOnAssignmentIds = null,
    string? ImpactReviewReason = null)
{
    public static AssignmentDto From(Assignment a) => new(
        a.Id, a.SeriesId, a.ChapterId, a.EntityType, a.TargetEntityId, a.PipelineStageId,
        a.AssigneeUserId, a.AssignedRole, a.Brief, a.ReferenceNotes, a.DueDate, a.Priority,
        a.Status, a.DependsOnAssignmentId, a.StartedAt, a.CompletedAt, a.LastFeedbackComment,
        a.CreatedAt, a.UpdatedAt, a.GetDependsOnAssignmentIds(), a.ImpactReviewReason);
}

/// <summary>DTO dành cho màn hình làm việc cá nhân xuyên suốt toàn bộ Studio (Cross-Chapter Worklist).</summary>
public sealed record WorkspaceAssignmentDto(
    Guid Id,
    Guid SeriesId,
    string SeriesTitle,
    Guid ChapterId,
    decimal ChapterNumber,
    string? ChapterTitle,
    Guid PipelineStageId,
    string StageName,
    int StageOrder,
    string? StageColorCode,
    WorkflowEntityType EntityType,
    Guid TargetEntityId,
    Guid AssigneeUserId,
    WorkspaceRole AssignedRole,
    string Brief,
    string? ReferenceNotes,
    DateTime? DueDate,
    AssignmentPriority Priority,
    AssignmentStatus Status,
    Guid? DependsOnAssignmentId,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? LastFeedbackComment,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<Guid>? DependsOnAssignmentIds = null,
    // Nhãn phạm vi để hiển thị: "Cả chương" | "Trang 2 · Cảnh 3, 4" | "Trang 2 · Khung 1 · Cảnh 3"
    string? ScopeLabel = null);

/// <summary>TargetEntityId bỏ trống khi EntityType = Chapter (mặc định là chính Chapter trên route).</summary>
public sealed record CreateAssignmentRequest(
    WorkflowEntityType EntityType,
    Guid? TargetEntityId,
    Guid PipelineStageId,
    Guid AssigneeUserId,
    string Brief,
    string? ReferenceNotes = null,
    DateTime? DueDate = null,
    AssignmentPriority Priority = AssignmentPriority.Medium,
    Guid? DependsOnAssignmentId = null,
    IReadOnlyList<Guid>? DependsOnAssignmentIds = null);

/// <summary>
/// Giao hàng loạt theo AssignmentGranularity của stage. PageIds null/rỗng = mọi trang của chương (bỏ qua khi stage giao cả chương).
/// DryRun = true: chỉ trả kế hoạch + xung đột, không lưu gì.
/// </summary>
public sealed record CreateBulkAssignmentRequest(
    Guid PipelineStageId,
    Guid AssigneeUserId,
    string Brief,
    IReadOnlyList<Guid>? PageIds = null,
    string? ReferenceNotes = null,
    DateTime? DueDate = null,
    AssignmentPriority Priority = AssignmentPriority.Medium,
    bool DryRun = false);

/// <summary>
/// Một việc trong đợt. PageNumber null với việc cả chương; ConflictReason khác null = đối tượng này không giao được.
/// DependsOnAssignmentIds: việc ở stage đang bật ngay trước mà việc này tự phụ thuộc (cùng trang, hoặc việc cả chương).
/// </summary>
public sealed record BulkAssignmentItemDto(
    WorkflowEntityType EntityType,
    Guid TargetEntityId,
    int? PageNumber,
    string? ConflictReason,
    Guid? AssignmentId,
    IReadOnlyList<Guid>? DependsOnAssignmentIds = null);

/// <summary>
/// Ứng viên nhận việc, đã xếp hạng (phần tử đầu = gợi ý tốt nhất). ActiveAssignmentCount: việc Assigned/InProgress/ReworkRequired/Blocked
/// trong Workspace; StageExperienceCount: số việc người này từng nhận ở stage này của Series; Reasons: lý do ngắn để hiện trên UI.
/// </summary>
public sealed record AssigneeSuggestionDto(
    Guid UserId,
    string FullName,
    string? AvatarUrl,
    WorkspaceRole Role,
    int ActiveAssignmentCount,
    int OverdueCount,
    int StageExperienceCount,
    bool IsSeriesMember,
    IReadOnlyList<string> Reasons);

/// <summary>BatchId null khi DryRun. Items luôn đủ mọi đối tượng trong đợt, theo thứ tự trang.</summary>
public sealed record BulkAssignmentResultDto(
    Guid? BatchId,
    bool DryRun,
    WorkflowEntityType Granularity,
    int CreatedCount,
    IReadOnlyList<BulkAssignmentItemDto> Items);

/// <summary>Một yêu cầu sửa cụ thể. RegionJson tuỳ chọn: <c>{ "x", "y", "w", "h" }</c> chuẩn hóa 0–1.</summary>
/// <summary>AssetReferenceId: phiên bản artwork (của Chapter) mà RegionJson khoanh trên đó; PanelId: Panel của vùng (thuộc Chapter). Đều tuỳ chọn.</summary>
public sealed record ChangeRequestItemRequest(
    string Description, string? RegionJson = null, DateTime? DueDate = null, Guid? AssetReferenceId = null,
    Guid? PanelId = null);

/// <summary>
/// Trả về bắt buộc có comment (BR-06). Changes bỏ trống = tạo một ChangeRequest từ chính comment.
/// </summary>
public sealed record RequestAssignmentChangesRequest(
    string? Comment, IReadOnlyList<ChangeRequestItemRequest>? Changes = null, Guid? ExpectedSubmissionId = null);

/// <summary>Body tuỳ chọn khi duyệt: ExpectedSubmissionId = lần nộp Editor đang xem; lệch lần nộp đang chờ thì 409.</summary>
public sealed record ApproveAssignmentRequest(Guid? ExpectedSubmissionId = null);

/// <summary>Người chịu trách nhiệm báo đã sửa, ghi chú tuỳ chọn.</summary>
public sealed record ResolveChangeRequestRequest(string? Note);

/// <summary>Dùng cho mở lại (comment) và từ chối (lý do): đều bắt buộc.</summary>
public sealed record ChangeRequestCommentRequest(string? Comment);

public sealed record SubmissionDto(
    Guid Id,
    Guid AssignmentId,
    Guid ChapterId,
    Guid SubmittedByUserId,
    int AttemptNumber,
    SubmissionStatus Status,
    DateTime SubmittedAt,
    Guid? DecidedByUserId,
    DateTime? DecidedAt,
    string? DecisionComment,
    IReadOnlyList<Guid>? ArtworkReferenceIds = null,  // phiên bản artwork (AssetReference.id) của lần nộp này
    long ChapterVersion = 0,                          // version chương lúc nộp (revision script)
    IReadOnlyList<SubmissionPanelDto>? PanelLayout = null, // bố cục Panel lúc nộp; rỗng = không có (lần nộp cũ / việc không có Panel)
    IReadOnlyDictionary<Guid, long>? CanvasVersions = null, // pageId → version canvas lúc nộp; rỗng = lần nộp cũ
    IReadOnlyList<Guid>? ScriptRevisionIds = null)           // revision script (mỗi Scene) lúc nộp; rỗng = lần nộp cũ
{
    public static SubmissionDto From(Submission s) => new(
        s.Id, s.AssignmentId, s.ChapterId, s.SubmittedByUserId, s.AttemptNumber, s.Status, s.SubmittedAt,
        s.DecidedByUserId, s.DecidedAt, s.DecisionComment, s.ArtworkReferenceIds, s.ChapterVersion,
        SubmissionPanelDto.Parse(s.PanelLayoutJson), ParseCanvasVersions(s.CanvasVersionsJson), s.ScriptRevisionIds);

    private static IReadOnlyDictionary<Guid, long> ParseCanvasVersions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<Guid, long>();
        try { return JsonSerializer.Deserialize<Dictionary<Guid, long>>(json) ?? new Dictionary<Guid, long>(); }
        catch (JsonException) { return new Dictionary<Guid, long>(); }
    }
}

/// <summary>Một Panel trong bố cục chụp lúc nộp. Cùng tên field với <c>PanelDto</c> để FE ghép trang bằng một kiểu dữ liệu.</summary>
public sealed record SubmissionPanelDto(Guid Id, Guid PageId, int PanelNumber, int ReadingOrder, string BoundingBox)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static string Serialize(IEnumerable<Panel> panels) => JsonSerializer.Serialize(
        panels.OrderBy(p => p.PageId).ThenBy(p => p.PanelNumber)
            .Select(p => new SubmissionPanelDto(p.Id, p.PageId, p.PanelNumber, p.ReadingOrder, p.BoundingBox)), JsonOpts);

    /// <summary>JSON hỏng hoặc rỗng → danh sách rỗng (FE quay về bố cục hiện tại), không làm hỏng cả lịch sử nộp.</summary>
    public static IReadOnlyList<SubmissionPanelDto> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<SubmissionPanelDto>>(json, JsonOpts) ?? []; }
        catch (JsonException) { return []; }
    }
}

public sealed record ChangeRequestDto(
    Guid Id,
    Guid AssignmentId,
    Guid ChapterId,
    Guid? SubmissionId,
    Guid ResponsibleUserId,
    Guid CreatedByUserId,
    string Description,
    string? RegionJson,
    DateTime? DueDate,
    ChangeRequestStatus Status,
    string? ResolutionNote,
    DateTime? ResolvedAt,
    string? ReviewerComment,
    Guid? ClosedByUserId,
    DateTime? ClosedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? AssetReferenceId = null,  // phiên bản artwork mà vùng khoanh thuộc về
    Guid? PanelId = null)           // Panel của vùng khoanh
{
    public static ChangeRequestDto From(ChangeRequest c) => new(
        c.Id, c.AssignmentId, c.ChapterId, c.SubmissionId, c.ResponsibleUserId, c.CreatedByUserId, c.Description,
        c.RegionJson, c.DueDate, c.Status, c.ResolutionNote, c.ResolvedAt, c.ReviewerComment, c.ClosedByUserId,
        c.ClosedAt, c.CreatedAt, c.UpdatedAt, c.AssetReferenceId, c.PanelId);
}

/// <summary>PUT thay toàn bộ: DependsOnAssignmentId null = bỏ dependency; ReferenceNotes/DueDate null = xoá.</summary>
public sealed record UpdateAssignmentRequest(
    string Brief,
    string? ReferenceNotes,
    DateTime? DueDate,
    AssignmentPriority Priority,
    Guid AssigneeUserId,
    Guid? DependsOnAssignmentId,
    IReadOnlyList<Guid>? DependsOnAssignmentIds = null);

/// <summary>Một dòng của kế hoạch giao: stage + người nhận. Brief/DueDate null = dùng giá trị chung của kế hoạch.</summary>
public sealed record AssignmentPlanStageRequest(
    Guid PipelineStageId,
    Guid AssigneeUserId,
    string? Brief = null,
    IReadOnlyList<Guid>? PageIds = null,
    DateTime? DueDate = null);

/// <summary>Giao nhiều stage một lần. Stage theo trang khi chương chưa có trang thì bị bỏ qua (SkippedReason), không lỗi.</summary>
public sealed record CreateAssignmentPlanRequest(
    IReadOnlyList<AssignmentPlanStageRequest> Stages,
    string Brief,
    string? ReferenceNotes = null,
    DateTime? DueDate = null,
    AssignmentPriority Priority = AssignmentPriority.Medium,
    bool DryRun = false);

public sealed record AssignmentPlanStageResultDto(
    Guid PipelineStageId,
    string StageName,
    Guid AssigneeUserId,
    WorkflowEntityType? Granularity, // null khi stage bị bỏ qua
    string? SkippedReason,
    IReadOnlyList<BulkAssignmentItemDto> Items);

public sealed record AssignmentPlanResultDto(
    Guid? BatchId,
    bool DryRun,
    int CreatedCount,
    IReadOnlyList<AssignmentPlanStageResultDto> Stages);

/// <summary>Mẫu giao việc lấy từ chương gần nhất của Series đã có việc. SourceChapterId null = chưa có mẫu.</summary>
public sealed record AssignmentPlanTemplateDto(
    Guid? SourceChapterId,
    decimal? SourceChapterNumber,
    IReadOnlyList<AssignmentPlanTemplateStageDto> Stages);

public sealed record AssignmentPlanTemplateStageDto(
    Guid PipelineStageId,
    string StageName,
    int StageOrder,
    Guid AssigneeUserId,
    string AssigneeName,
    string? Brief);

/// <summary>DTO dành cho Hàng đợi thẩm định Biên tập viên (Editor Desk / Review Inbox) gom bài từ mọi Chapter.</summary>
public sealed record EditorReviewQueueItemDto(
    Guid AssignmentId,
    Guid ChapterId,
    decimal ChapterNumber,
    string? ChapterTitle,
    Guid SeriesId,
    string SeriesTitle,
    Guid PipelineStageId,
    string StageName,
    int StageOrder,
    string? StageColorCode,
    string Gate, // "Gate 1: Storyboard" | "Gate 2: Quality & Composite"
    AssignmentStatus Status,
    WorkflowEntityType EntityType,
    Guid TargetEntityId,
    string? ScopeLabel,
    Guid AssigneeUserId,
    string AssigneeName,
    string? AssigneeAvatarUrl,
    Guid? LatestSubmissionId,
    int SubmissionAttempt,
    DateTime? SubmittedAt,
    bool IsSelfSubmission, // Tuân thủ BR-26 (4-Eyes Principle)
    int OpenChangeRequestsCount,
    DateTime? DueDate,
    AssignmentPriority Priority,
    DateTime CreatedAt,
    IReadOnlyList<Guid>? ArtworkReferenceIds = null);
