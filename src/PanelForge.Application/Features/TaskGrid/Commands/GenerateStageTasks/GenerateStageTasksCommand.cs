using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Commands.GenerateStageTasks;

/// <summary>
/// Producer sinh các ô còn thiếu của lưới: mỗi công đoạn đang bật cấp Chương một ô, cấp Trang mỗi trang một ô.
/// Gọi lại nhiều lần an toàn: ô đã có giữ nguyên (theo Page.Id, không theo số trang), ô của trang đã xoá chuyển Cancelled.
/// </summary>
public sealed record GenerateStageTasksCommand(Guid ChapterId, Guid UserId) : IRequest<Result<GenerateStageTasksResultDto>>;

public sealed class GenerateStageTasksCommandHandler : IRequestHandler<GenerateStageTasksCommand, Result<GenerateStageTasksResultDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GenerateStageTasksCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<GenerateStageTasksResultDto>> Handle(GenerateStageTasksCommand cmd, CancellationToken ct)
    {
        static Result<GenerateStageTasksResultDto> Fail(string msg, string code) => Result<GenerateStageTasksResultDto>.Failure(msg, code);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var stages = (await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct)).Where(s => s.IsEnabled).ToList();
        if (stages.Count == 0) return Fail("Series chưa có công đoạn nào đang bật.", ResultErrorCodes.Conflict);

        var pageIds = await _db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id).Select(p => p.Id).ToListAsync(ct);
        var existing = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);

        var created = 0;
        foreach (var stage in stages)
        {
            IEnumerable<Guid?> targets = stage.AssignmentGranularity == WorkflowEntityType.Chapter
                ? new Guid?[] { null }
                : pageIds.Select(id => (Guid?)id);
            foreach (var pageId in targets)
            {
                if (existing.Any(t => t.PipelineStageId == stage.Id && t.PageId == pageId)) continue;
                var task = StageTask.Create(chapter.SeriesId, chapter.Id, stage.Id, pageId);
                task.CreatedBy = cmd.UserId.ToString();
                _db.StageTasks.Add(task);
                existing.Add(task);
                created++;
            }
        }

        var orphaned = existing
            .Where(t => t.PageId is { } pid && !pageIds.Contains(pid) && t.Status != StageTaskStatus.Cancelled)
            .ToList();
        foreach (var task in orphaned) task.Cancel();
        await TaskGridRules.RefreshLotsAsync(_db, orphaned.Select(t => t.LotId), orphaned, ct);

        await _db.SaveChangesAsync(ct);
        return Result<GenerateStageTasksResultDto>.Success(
            new GenerateStageTasksResultDto(created, orphaned.Count, existing.Count(t => t.Status != StageTaskStatus.Cancelled)));
    }
}
