using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Queries.GetChapterTaskGrid;

/// <summary>Lưới Trang × Công đoạn của một chương: công đoạn kèm tiền đề theo nhánh, trang, ô và lô.</summary>
public sealed record GetChapterTaskGridQuery(Guid ChapterId) : IRequest<Result<TaskGridDto>>;

public sealed class GetChapterTaskGridQueryHandler : IRequestHandler<GetChapterTaskGridQuery, Result<TaskGridDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetChapterTaskGridQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<TaskGridDto>> Handle(GetChapterTaskGridQuery q, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == q.ChapterId, ct);
        if (chapter is null)
            return Result<TaskGridDto>.Failure($"Chapter {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var stages = await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct);
        var pages = await _db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id)
            .OrderBy(p => p.PageNumber).Select(p => new TaskGridPageDto(p.Id, p.PageNumber)).ToListAsync(ct);
        var tasks = await _db.StageTasks.AsNoTracking().Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);
        var lots = await _db.StageTaskLots.AsNoTracking().Where(l => l.ChapterId == chapter.Id).ToListAsync(ct);
        var assigneeByLot = lots.ToDictionary(l => l.Id, l => l.AssigneeUserId);

        var stageDtos = stages.Select(s => new TaskGridStageDto(
            s.Id, s.Name, s.StageOrder, s.Track, s.AllowedRole, s.IsApprovalGate, s.AssignmentGranularity, s.IsEnabled,
            s.IsEnabled ? StageDependencies.DirectPrerequisites(s, stages).Select(p => p.Id).ToList() : [])).ToList();

        var taskDtos = tasks.Select(t => StageTaskDto.From(
            t,
            t.LotId is { } lotId && assigneeByLot.TryGetValue(lotId, out var assignee) ? assignee : null,
            t.Status is StageTaskStatus.Assigned or StageTaskStatus.ReworkRequired && !t.IsInputOutdated
                && TaskGridRules.PrerequisitesApproved(t, stages, tasks))).ToList();

        var now = DateTime.UtcNow;
        var lotDtos = lots.Select(l => StageTaskLotDto.From(l, tasks.Where(t => t.LotId == l.Id).ToList(), now)).ToList();

        return Result<TaskGridDto>.Success(new TaskGridDto(chapter.Id, stageDtos, pages, taskDtos, lotDtos));
    }
}
