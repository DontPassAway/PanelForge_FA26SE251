using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Workflow;

namespace PanelForge.Application.Features.Assignments.Commands.CreateAssignmentPlan;

/// <summary>
/// Giao cả chương cho nhiều stage một lần (Script → Artist → Letterer...). Mỗi stage đi qua đúng các luật của giao hàng loạt
/// (AssignmentBatchPlanner); việc của stage sau tự phụ thuộc việc vừa dựng của stage trước trong cùng kế hoạch (BR-19).
/// All-or-nothing: một trang trùng ở bất kỳ stage nào → 409, không lưu gì. Cả kế hoạch chung một BatchId,
/// mỗi người nhận đúng 1 thông báo (BR-14). Stage theo trang khi chương chưa có trang → bỏ qua kèm lý do, giao sau.
/// </summary>
public sealed record CreateAssignmentPlanCommand(Guid ChapterId, Guid UserId, CreateAssignmentPlanRequest Body)
    : IRequest<Result<AssignmentPlanResultDto>>;

public sealed class CreateAssignmentPlanCommandHandler : IRequestHandler<CreateAssignmentPlanCommand, Result<AssignmentPlanResultDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IUnitOfWork _unitOfWork;

    public CreateAssignmentPlanCommandHandler(IPanelForgeDbContext db, IUnitOfWork unitOfWork)
    {
        _db = db;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AssignmentPlanResultDto>> Handle(CreateAssignmentPlanCommand cmd, CancellationToken ct)
    {
        static Result<AssignmentPlanResultDto> Fail(string msg, string? code = ResultErrorCodes.Validation) =>
            Result<AssignmentPlanResultDto>.Failure(msg, code);

        var body = cmd.Body;
        if (body.Stages is not { Count: > 0 })
            return Fail("Kế hoạch phải có ít nhất một stage.");
        if (string.IsNullOrWhiteSpace(body.Brief) && body.Stages.Any(s => string.IsNullOrWhiteSpace(s.Brief)))
            return Fail("Brief không được để trống.");
        if (body.Stages.GroupBy(s => s.PipelineStageId).Any(g => g.Count() > 1))
            return Fail("Mỗi stage chỉ xuất hiện một lần trong kế hoạch.");

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Fail(locked, ResultErrorCodes.Conflict);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);

        var resolved = new List<(AssignmentPlanStageRequest Req, AssignmentBatchPlanner.StageBatch? Batch, string? Skipped, string StageName, int Order)>();
        foreach (var req in body.Stages)
        {
            var r = await AssignmentBatchPlanner.ResolveAsync(_db, chapter, workspaceId, req.PipelineStageId, req.AssigneeUserId, req.PageIds, ct);
            if (r.IsSuccess)
            {
                resolved.Add((req, r.Value, null, r.Value!.Stage.Name, r.Value.Stage.StageOrder));
                continue;
            }
            // Chỉ "chưa có trang" là bỏ qua được; sai stage/sai vai trò vẫn là lỗi của cả kế hoạch
            var stage = await _db.PipelineStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.PipelineStageId, ct);
            if (stage is null || !r.ErrorMessage!.StartsWith(AssignmentBatchPlanner.NoPagesPrefix))
                return Fail(r.ErrorMessage!, r.ErrorCode);
            resolved.Add((req, null, r.ErrorMessage, stage.Name, stage.StageOrder));
        }
        // Dựng theo StageOrder để stage sau thấy việc vừa dựng của stage trước
        var ordered = resolved.OrderBy(x => x.Order).ToList();

        async Task<(List<AssignmentPlanStageResultDto> Stages, List<Assignment> Created)> BuildAsync(Guid batchId, CancellationToken tct)
        {
            var pending = new List<Assignment>();
            var stages = new List<AssignmentPlanStageResultDto>();
            foreach (var x in ordered)
            {
                if (x.Batch is null)
                {
                    stages.Add(new(x.Req.PipelineStageId, x.StageName, x.Req.AssigneeUserId, null, x.Skipped, []));
                    continue;
                }
                var plan = await AssignmentBatchPlanner.PlanAsync(_db, chapter.Id, x.Batch, pending, tct);
                var built = AssignmentBatchPlanner.Build(chapter, x.Batch, plan, x.Req.AssigneeUserId, cmd.UserId,
                    string.IsNullOrWhiteSpace(x.Req.Brief) ? body.Brief : x.Req.Brief,
                    body.ReferenceNotes, x.Req.DueDate ?? body.DueDate, body.Priority, batchId);
                pending.AddRange(built);
                stages.Add(new(x.Batch.Stage.Id, x.StageName, x.Req.AssigneeUserId, x.Batch.Stage.AssignmentGranularity, null,
                    plan.Select((i, idx) => i with { AssignmentId = built[idx].Id }).ToList()));
            }
            return (stages, pending);
        }

        if (body.DryRun)
        {
            // Phụ thuộc trong bản xem trước trỏ tới id tạm của việc chưa tạo; AssignmentId để null
            var (preview, _) = await BuildAsync(Guid.Empty, ct);
            var cleared = preview.Select(s => s with { Items = s.Items.Select(i => i with { AssignmentId = null }).ToList() }).ToList();
            return Result<AssignmentPlanResultDto>.Success(new AssignmentPlanResultDto(null, true, 0, cleared));
        }

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async tct =>
            {
                await _db.LockChapterAsync(chapter.Id, tct);

                var batchId = Guid.NewGuid();
                var (stages, created) = await BuildAsync(batchId, tct);
                foreach (var s in stages)
                {
                    var conflicts = s.Items.Where(i => i.ConflictReason is not null).ToList();
                    if (conflicts.Count > 0)
                        return Fail($"Stage {s.StageName}: {AssignmentBatchPlanner.DescribeConflicts(conflicts)}", ResultErrorCodes.Conflict);
                }
                if (created.Count == 0)
                    return Fail("Không có stage nào giao được. " + string.Join(" ", stages.Select(s => s.SkippedReason)));

                foreach (var a in created) _db.Assignments.Add(a);
                foreach (var x in ordered.Where(x => x.Batch is not null))
                    await AssignmentBatchPlanner.LinkLaterChapterWorkAsync(_db, chapter.Id, x.Batch!.Stage,
                        created.Where(a => a.PipelineStageId == x.Batch.Stage.Id).ToList(), created, tct);
                foreach (var x in ordered.Where(x => x.Batch is not null).DistinctBy(x => x.Req.AssigneeUserId))
                    await AssignmentRules.EnsureSeriesMemberAsync(_db, chapter.SeriesId, x.Req.AssigneeUserId, x.Batch!.Role, tct);
                var stageNames = ordered.ToDictionary(x => x.Req.PipelineStageId, x => x.StageName);
                foreach (var mine in created.GroupBy(a => a.AssigneeUserId))
                    _db.Notifications.Add(AssignmentNotifications.AssignedPlan(mine.ToList(), stageNames));
                await _db.SaveChangesAsync(tct);

                return Result<AssignmentPlanResultDto>.Success(new AssignmentPlanResultDto(batchId, false, created.Count, stages));
            }, ct);
        }
        catch (DbUpdateException)
        {
            return Fail("Một phần của kế hoạch vừa được giao bởi thao tác khác. Vui lòng xem trước và giao lại.", ResultErrorCodes.Conflict);
        }
    }
}
