using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.CreateBulkAssignment;

/// <summary>
/// Giao hàng loạt (docs/diagrams/assignment-architecture.html): stage.AssignmentGranularity quyết định 1 việc cả chương hay
/// N việc theo trang. Khoá dòng Chapter (FOR UPDATE) rồi kiểm tra trùng, 1 trang trùng → huỷ cả đợt (409); uq_active_assignment
/// chặn lần cuối ở DB. Mỗi đợt có một BatchId và đúng 1 thông báo (BR-14). Mỗi việc tự phụ thuộc việc của stage đang bật ngay
/// trước phủ cùng phạm vi (BR-19), nên người nhận thấy rõ đang chờ việc nào.
/// </summary>
public sealed record CreateBulkAssignmentCommand(
    Guid ChapterId,
    Guid UserId,
    Guid PipelineStageId,
    Guid AssigneeUserId,
    string Brief,
    IReadOnlyList<Guid>? PageIds,
    string? ReferenceNotes,
    DateTime? DueDate,
    AssignmentPriority Priority,
    bool DryRun
) : IRequest<Result<BulkAssignmentResultDto>>;

public sealed class CreateBulkAssignmentCommandHandler : IRequestHandler<CreateBulkAssignmentCommand, Result<BulkAssignmentResultDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IUnitOfWork _unitOfWork;

    public CreateBulkAssignmentCommandHandler(IPanelForgeDbContext db, IUnitOfWork unitOfWork)
    {
        _db = db;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BulkAssignmentResultDto>> Handle(CreateBulkAssignmentCommand cmd, CancellationToken ct)
    {
        static Result<BulkAssignmentResultDto> Fail(string msg, string? code = ResultErrorCodes.Validation) =>
            Result<BulkAssignmentResultDto>.Failure(msg, code);

        if (string.IsNullOrWhiteSpace(cmd.Brief))
            return Fail("Brief không được để trống.");

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Fail(locked, ResultErrorCodes.Conflict);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);

        var resolved = await AssignmentBatchPlanner.ResolveAsync(
            _db, chapter, workspaceId, cmd.PipelineStageId, cmd.AssigneeUserId, cmd.PageIds, ct);
        if (!resolved.IsSuccess) return Fail(resolved.ErrorMessage!, resolved.ErrorCode);
        var batch = resolved.Value!;
        var stage = batch.Stage;

        if (cmd.DryRun)
        {
            var preview = await AssignmentBatchPlanner.PlanAsync(_db, chapter.Id, batch, [], ct);
            return Result<BulkAssignmentResultDto>.Success(
                new BulkAssignmentResultDto(null, true, stage.AssignmentGranularity, 0, preview));
        }

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async tct =>
            {
                // Hai Producer giao cùng chương cùng lúc: người sau chờ ở đây, rồi thấy việc của người trước khi kiểm tra trùng
                await _db.LockChapterAsync(chapter.Id, tct);

                var plan = await AssignmentBatchPlanner.PlanAsync(_db, chapter.Id, batch, [], tct);
                var conflicts = plan.Where(i => i.ConflictReason is not null).ToList();
                if (conflicts.Count > 0)
                    return Fail(AssignmentBatchPlanner.DescribeConflicts(conflicts), ResultErrorCodes.Conflict);

                var batchId = Guid.NewGuid();
                var created = AssignmentBatchPlanner.Build(chapter, batch, plan, cmd.AssigneeUserId, cmd.UserId, cmd.Brief,
                    cmd.ReferenceNotes, cmd.DueDate, cmd.Priority, batchId);

                foreach (var a in created) _db.Assignments.Add(a);
                await AssignmentBatchPlanner.LinkLaterChapterWorkAsync(_db, chapter.Id, stage, created, created, tct);
                await AssignmentRules.EnsureSeriesMemberAsync(_db, chapter.SeriesId, cmd.AssigneeUserId, batch.Role, tct);
                _db.Notifications.Add(AssignmentNotifications.AssignedBatch(created, stage.Name));
                await _db.SaveChangesAsync(tct);

                var items = plan.Select((i, idx) => i with { AssignmentId = created[idx].Id }).ToList();
                return Result<BulkAssignmentResultDto>.Success(
                    new BulkAssignmentResultDto(batchId, false, stage.AssignmentGranularity, created.Count, items));
            }, ct);
        }
        catch (DbUpdateException)
        {
            // uq_active_assignment hoặc unique (series_id, user_id) của membership: cả đợt không được lưu
            return Fail("Một phần của đợt vừa được giao bởi thao tác khác. Vui lòng xem trước và giao lại.", ResultErrorCodes.Conflict);
        }
    }
}
