using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Commands.GenerateStageTasks;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Commands.MigrateAssignmentsToGrid;

/// <summary>
/// Clean Cutover (lát cắt 4): chuyển mọi Assignment V2 còn sống của Series sang lưới V3. Mỗi chương: sinh ô, rồi mỗi việc cũ
/// thành một lô gồm các ô nó phủ (việc cấp Chương của công đoạn cấp Trang phủ mọi trang; việc cấp Khung lấy trang của khung).
/// Việc đã chuyển được xoá mềm (giữ lịch sử, BR-01). Việc không chuyển được (công đoạn tắt, ô đã có người) giữ nguyên và báo lại.
/// Submitted/UnderReview về InProgress vì V2 không có ảnh chụp lần nộp theo ô: người làm nộp lại. Gọi lại an toàn.
/// </summary>
public sealed record MigrateAssignmentsToGridCommand(Guid SeriesId, Guid UserId) : IRequest<Result<MigrateAssignmentsResultDto>>;

public sealed class MigrateAssignmentsToGridCommandHandler
    : IRequestHandler<MigrateAssignmentsToGridCommand, Result<MigrateAssignmentsResultDto>>
{
    public const string CancelReason = "Đã chuyển sang lưới giao việc V3.";

    private readonly IPanelForgeDbContext _db;
    private readonly ISender _sender;

    public MigrateAssignmentsToGridCommandHandler(IPanelForgeDbContext db, ISender sender)
    {
        _db = db;
        _sender = sender;
    }

    public async Task<Result<MigrateAssignmentsResultDto>> Handle(MigrateAssignmentsToGridCommand cmd, CancellationToken ct)
    {
        var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null)
            return Result<MigrateAssignmentsResultDto>.Failure($"Series {cmd.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        var chapters = await _db.Chapters.AsNoTracking().Where(c => c.SeriesId == series.Id)
            .OrderBy(c => c.ChapterNumber).ToListAsync(ct);
        var stages = await TaskGridRules.LoadStagesAsync(_db, series.Id, ct);
        var skipped = new List<MigrateSkippedDto>();
        int migrated = 0, cells = 0;

        foreach (var chapter in chapters)
        {
            var assignments = await _db.Assignments.Where(a => a.ChapterId == chapter.Id).ToListAsync(ct);
            if (assignments.Count == 0) continue;

            if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            {
                skipped.AddRange(assignments.Select(a => new MigrateSkippedDto(a.Id, chapter.Id, locked)));
                continue;
            }

            var generated = await _sender.Send(new GenerateStageTasksCommand(chapter.Id, cmd.UserId), ct);
            if (!generated.IsSuccess)
            {
                skipped.AddRange(assignments.Select(a => new MigrateSkippedDto(a.Id, chapter.Id, generated.ErrorMessage ?? "Không sinh được ô.")));
                continue;
            }

            var tasks = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);
            var pageIds = await _db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id).Select(p => p.Id).ToListAsync(ct);
            var panelIds = assignments.Where(a => a.EntityType == WorkflowEntityType.Panel).Select(a => a.TargetEntityId).ToList();
            var pageOfPanel = await _db.Panels.AsNoTracking().Where(p => panelIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.PageId, ct);

            // Việc hẹp (Khung, Trang) trước việc rộng (Chương) để ô thuộc đúng người làm trang đó
            foreach (var a in assignments.OrderBy(a => a.EntityType == WorkflowEntityType.Chapter).ThenBy(a => a.CreatedAt))
            {
                var stage = stages.FirstOrDefault(s => s.Id == a.PipelineStageId);
                if (stage is null || !stage.IsEnabled)
                {
                    skipped.Add(new MigrateSkippedDto(a.Id, chapter.Id, "Công đoạn đã tắt hoặc không còn trong pipeline."));
                    continue;
                }

                List<Guid?> targetPages = stage.AssignmentGranularity == WorkflowEntityType.Chapter
                    ? [null]
                    : a.EntityType switch
                    {
                        WorkflowEntityType.Page => [a.TargetEntityId],
                        WorkflowEntityType.Panel when pageOfPanel.TryGetValue(a.TargetEntityId, out var pid) => [pid],
                        WorkflowEntityType.Chapter => pageIds.Select(id => (Guid?)id).ToList(),
                        _ => []
                    };
                var targets = tasks.Where(t => t.PipelineStageId == stage.Id && targetPages.Contains(t.PageId)
                    && t.Status == StageTaskStatus.Unassigned).ToList();
                if (targets.Count == 0)
                {
                    skipped.Add(new MigrateSkippedDto(a.Id, chapter.Id, targetPages.Count == 0
                        ? "Không xác định được trang của việc."
                        : "Ô tương ứng đã có người nhận trên lưới."));
                    continue;
                }

                var lot = StageTaskLot.Create(series.Id, chapter.Id, stage.Id, a.AssigneeUserId, a.AssignedRole, cmd.UserId, a.Brief, a.DueDate);
                lot.CreatedBy = cmd.UserId.ToString();
                _db.StageTaskLots.Add(lot);

                var status = a.Status switch
                {
                    AssignmentStatus.Approved => StageTaskStatus.Approved,
                    AssignmentStatus.ReworkRequired => StageTaskStatus.ReworkRequired,
                    AssignmentStatus.InProgress or AssignmentStatus.Submitted or AssignmentStatus.UnderReview => StageTaskStatus.InProgress,
                    _ => StageTaskStatus.Assigned
                };
                foreach (var task in targets)
                {
                    task.RestoreFromLegacy(lot.Id, status, a.StartedAt);
                    if (a.ImpactReviewReason is { Length: > 0 } reason) task.MarkInputOutdated(reason);
                    task.UpdatedBy = cmd.UserId.ToString();
                }
                lot.RefreshStatus(targets.Select(t => t.Status).ToList());

                if (a.Status != AssignmentStatus.Approved) a.Cancel(CancelReason);
                _db.Assignments.Remove(a); // xoá mềm (ISoftDelete), lịch sử V2 vẫn còn
                migrated++;
                cells += targets.Count;
            }

            await _db.SaveChangesAsync(ct);
        }

        return Result<MigrateAssignmentsResultDto>.Success(new MigrateAssignmentsResultDto(migrated, cells, skipped));
    }
}
