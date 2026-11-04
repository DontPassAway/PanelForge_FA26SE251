using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Common;

/// <summary>
/// Lõi dùng chung của giao hàng loạt (1 stage) và giao theo kế hoạch (nhiều stage): chọn đối tượng theo
/// stage.AssignmentGranularity, kiểm tra trùng phạm vi, tự nối phụ thuộc vào việc của stage đang bật ngay trước (BR-19).
/// <c>pending</c> = việc đã dựng ở các stage trước trong cùng kế hoạch nhưng chưa lưu, để dryRun và chuỗi phụ thuộc
/// Script → Artist → Letterer đúng mà không phải Save giữa chừng.
/// </summary>
internal static class AssignmentBatchPlanner
{
    /// <summary>Đầu thông báo lỗi khi stage theo trang mà chương chưa có trang (kế hoạch bỏ qua stage này thay vì báo lỗi).</summary>
    public const string NoPagesPrefix = "Chapter chưa có trang nào";

    public readonly record struct Target(WorkflowEntityType Type, Guid Id, int? PageNumber);

    public sealed record StageBatch(PipelineStage Stage, WorkspaceRole Role, List<Target> Targets);

    /// <summary>Stage thuộc pipeline của Series và đang bật, người nhận đúng vai, danh sách đối tượng không rỗng.</summary>
    public static async Task<Result<StageBatch>> ResolveAsync(
        IPanelForgeDbContext db, Chapter chapter, Guid workspaceId, Guid stageId, Guid assigneeId,
        IReadOnlyList<Guid>? pageIds, CancellationToken ct)
    {
        var pipelineIds = await db.PipelineDefinitions.AsNoTracking()
            .Where(d => d.SeriesId == chapter.SeriesId).Select(d => d.Id).ToListAsync(ct);
        var stage = await db.PipelineStages.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == stageId && pipelineIds.Contains(s.PipelineDefinitionId), ct);
        if (stage is null)
            return Result<StageBatch>.Failure("Stage không thuộc pipeline của Series.", ResultErrorCodes.Validation);
        if (!stage.IsEnabled)
            return Result<StageBatch>.Failure($"Stage {stage.Name} đang tắt cho Series.", ResultErrorCodes.Validation);

        var role = await AssignmentRules.ResolveAssigneeRoleAsync(db, workspaceId, assigneeId, stage, ct);
        if (!role.IsSuccess) return Result<StageBatch>.Failure(role.ErrorMessage!, role.ErrorCode);

        if (stage.AssignmentGranularity == WorkflowEntityType.Chapter)
            return Result<StageBatch>.Success(new StageBatch(stage, role.Value, [new(WorkflowEntityType.Chapter, chapter.Id, null)]));

        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapter.Id)
            .OrderBy(p => p.PageNumber)
            .Select(p => new { p.Id, p.PageNumber })
            .ToListAsync(ct);
        if (pageIds is { Count: > 0 } wanted)
        {
            var unknown = wanted.Except(pages.Select(p => p.Id)).ToList();
            if (unknown.Count > 0)
                return Result<StageBatch>.Failure($"{unknown.Count} trang không thuộc Chapter.", ResultErrorCodes.Validation);
            pages = pages.Where(p => wanted.Contains(p.Id)).ToList();
        }
        if (pages.Count == 0)
            return Result<StageBatch>.Failure($"{NoPagesPrefix} để giao stage {stage.Name}.", ResultErrorCodes.Validation);

        return Result<StageBatch>.Success(new StageBatch(stage, role.Value,
            pages.Select(p => new Target(WorkflowEntityType.Page, p.Id, p.PageNumber)).ToList()));
    }

    // ponytail: mỗi trang 1-2 truy vấn trùng phạm vi; chương vài chục trang thì ổn, gộp truy vấn nếu chương lên hàng trăm trang
    public static async Task<List<BulkAssignmentItemDto>> PlanAsync(
        IPanelForgeDbContext db, Guid chapterId, StageBatch batch, IReadOnlyList<Assignment> pending, CancellationToken ct)
    {
        var priors = await PriorStageAssignmentsAsync(db, chapterId, batch.Stage, pending, ct);
        var items = new List<BulkAssignmentItemDto>(batch.Targets.Count);
        foreach (var t in batch.Targets)
        {
            var reason = await AssignmentRules.ScopeOverlapReasonAsync(db, chapterId, batch.Stage, t.Type, t.Id, ct);
            items.Add(new BulkAssignmentItemDto(t.Type, t.Id, t.PageNumber, reason, null, DependenciesFor(t.Type, t.Id, priors)));
        }
        return items;
    }

    public static List<Assignment> Build(
        Chapter chapter, StageBatch batch, List<BulkAssignmentItemDto> plan, Guid assigneeId, Guid createdBy, string brief,
        string? referenceNotes, DateTime? dueDate, AssignmentPriority priority, Guid batchId) =>
        batch.Targets.Select((t, idx) =>
        {
            var a = Assignment.Create(
                chapter.SeriesId, chapter.Id, t.Type, t.Id, batch.Stage.Id,
                assigneeId, batch.Role, brief,
                referenceNotes: referenceNotes, dueDate: dueDate, priority: priority,
                dependsOnAssignmentIds: plan[idx].DependsOnAssignmentIds,
                batchId: batchId);
            a.CreatedBy = createdBy.ToString();
            return a;
        }).ToList();

    /// <summary>
    /// Nối bù: việc cả chương ở stage sau (vd Review) đã giao trước khi stage này có việc (stage bị bỏ qua vì chưa có trang)
    /// thì giờ phải chờ thêm việc vừa tạo. Chỉ việc còn Assigned, và chỉ khi giữa hai stage chưa có việc nào
    /// (có rồi thì nó đã chờ gián tiếp qua stage đó). <c>allNew</c> = mọi việc mới trong cùng lần giao, kể cả stage khác.
    /// </summary>
    public static async Task LinkLaterChapterWorkAsync(
        IPanelForgeDbContext db, Guid chapterId, PipelineStage stage, IReadOnlyList<Assignment> created,
        IReadOnlyList<Assignment> allNew, CancellationToken ct)
    {
        if (created.Count == 0) return;
        var laterStages = await db.PipelineStages.AsNoTracking()
            .Where(s => s.PipelineDefinitionId == stage.PipelineDefinitionId && s.StageOrder > stage.StageOrder && s.IsEnabled)
            .Select(s => new { s.Id, s.StageOrder })
            .ToListAsync(ct);
        if (laterStages.Count == 0) return;

        var laterIds = laterStages.Select(s => s.Id).ToList();
        var existing = await db.Assignments
            .Where(a => a.ChapterId == chapterId && a.CancelReason == null && laterIds.Contains(a.PipelineStageId))
            .ToListAsync(ct);
        var order = laterStages.ToDictionary(s => s.Id, s => s.StageOrder);
        var busyOrders = existing.Concat(allNew).Where(a => order.ContainsKey(a.PipelineStageId))
            .Select(a => order[a.PipelineStageId]).ToHashSet();

        foreach (var waiting in existing.Where(a => a.EntityType == WorkflowEntityType.Chapter && a.Status == AssignmentStatus.Assigned))
        {
            var target = order[waiting.PipelineStageId];
            if (busyOrders.Any(o => o > stage.StageOrder && o < target)) continue;
            waiting.SetDependencies(waiting.GetDependsOnAssignmentIds().Concat(created.Select(c => c.Id)));
        }
    }

    public static string DescribeConflicts(List<BulkAssignmentItemDto> conflicts)
    {
        if (conflicts.Count == 1 && conflicts[0].PageNumber is null)
            return conflicts[0].ConflictReason!;
        var pages = string.Join(", ", conflicts.Select(c => c.PageNumber));
        return $"Không giao được trang {pages}: {conflicts[0].ConflictReason} Không có việc nào được tạo.";
    }

    private sealed record PriorWork(Guid Id, WorkflowEntityType Type, Guid Target, Guid? PageId);

    /// <summary>Việc ở stage đang bật gần nhất phía trước (như BR-19 trong StageProgressRules); việc Panel kèm PageId của nó.</summary>
    private static async Task<List<PriorWork>> PriorStageAssignmentsAsync(
        IPanelForgeDbContext db, Guid chapterId, PipelineStage stage, IReadOnlyList<Assignment> pending, CancellationToken ct)
    {
        var prior = await db.PipelineStages.AsNoTracking()
            .Where(s => s.PipelineDefinitionId == stage.PipelineDefinitionId && s.StageOrder < stage.StageOrder && s.IsEnabled)
            .OrderByDescending(s => s.StageOrder)
            .FirstOrDefaultAsync(ct);
        if (prior is null) return [];

        var work = (await db.Assignments.AsNoTracking()
                .Where(a => a.ChapterId == chapterId && a.PipelineStageId == prior.Id)
                .Select(a => new { a.Id, a.EntityType, a.TargetEntityId })
                .ToListAsync(ct))
            .Concat(pending.Where(a => a.PipelineStageId == prior.Id)
                .Select(a => new { a.Id, a.EntityType, a.TargetEntityId }))
            .DistinctBy(w => w.Id)
            .ToList();
        var panelIds = work.Where(w => w.EntityType == WorkflowEntityType.Panel).Select(w => w.TargetEntityId).ToList();
        var panelPages = panelIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.Panels.AsNoTracking().Where(p => panelIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.PageId, ct);

        return work.Select(w => new PriorWork(w.Id, w.EntityType, w.TargetEntityId, w.EntityType switch
        {
            WorkflowEntityType.Page => w.TargetEntityId,
            WorkflowEntityType.Panel => panelPages.GetValueOrDefault(w.TargetEntityId),
            _ => null
        })).ToList();
    }

    /// <summary>Việc cả chương phụ thuộc mọi việc của stage trước; việc theo trang phụ thuộc việc cả chương + việc của chính trang đó.</summary>
    private static IReadOnlyList<Guid> DependenciesFor(WorkflowEntityType type, Guid target, List<PriorWork> priors) =>
        type == WorkflowEntityType.Chapter
            ? priors.Select(p => p.Id).ToList()
            : priors.Where(p => p.Type == WorkflowEntityType.Chapter || p.PageId == target).Select(p => p.Id).ToList();
}
