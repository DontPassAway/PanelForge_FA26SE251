using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Commands.ApproveStageTasks;

/// <summary>
/// Assign Automation N3: Editor/Producer duyệt nhiều ô một lần, tất cả hoặc không. Cùng luật với duyệt lẻ
/// (<see cref="ApproveStageTask.ApproveStageTaskCommand"/>): BR-06, không duyệt chồng ô người khác đang mở, không duyệt ô lỗi thời.
/// Ô vướng được báo đủ một lượt theo số trang. Thông báo Approved gom theo người làm và công đoạn.
/// </summary>
public sealed record ApproveStageTasksCommand(Guid ChapterId, IReadOnlyList<Guid> TaskIds, Guid UserId)
    : IRequest<Result<IReadOnlyList<StageTaskDto>>>, ITransactionalCommand;

public sealed class ApproveStageTasksCommandHandler : IRequestHandler<ApproveStageTasksCommand, Result<IReadOnlyList<StageTaskDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public ApproveStageTasksCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<StageTaskDto>>> Handle(ApproveStageTasksCommand cmd, CancellationToken ct)
    {
        static Result<IReadOnlyList<StageTaskDto>> Fail(string msg, string code = ResultErrorCodes.Validation) =>
            Result<IReadOnlyList<StageTaskDto>>.Failure(msg, code);

        var ids = cmd.TaskIds.Distinct().ToList();
        if (ids.Count == 0) return Fail("Cần ít nhất một ô để duyệt.");

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var chapterTasks = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);
        var tasks = chapterTasks.Where(t => ids.Contains(t.Id)).ToList();
        if (tasks.Count != ids.Count) return Fail("Có ô không thuộc chương này.", ResultErrorCodes.NotFound);

        var lotIds = tasks.Select(t => t.LotId).OfType<Guid>().Distinct().ToList();
        var lots = await _db.StageTaskLots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var submissionIds = tasks.Select(t => t.CurrentSubmissionId).OfType<Guid>().ToList();
        var pending = await _db.StageTaskSubmissions
            .Where(s => submissionIds.Contains(s.Id) && s.Status == SubmissionStatus.Pending).ToDictionaryAsync(s => s.Id, ct);
        var pageIds = tasks.Select(t => t.PageId).OfType<Guid>().Distinct().ToList();
        var pageNumbers = await _db.Pages.AsNoTracking().Where(p => pageIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.PageNumber, ct);
        var stages = await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct);
        string StageName(StageTask t) => stages.FirstOrDefault(s => s.Id == t.PipelineStageId)?.Name ?? "?";
        string Label(StageTask t) => t.PageId is { } p ? $"Trang {pageNumbers.GetValueOrDefault(p)} – {StageName(t)}" : $"{StageName(t)} (cả chương)";

        // Kiểm tra hết trước khi đổi gì, báo mọi ô vướng một lượt
        var blocked = new List<(int Order, string Message, string Code)>();
        foreach (var task in tasks)
        {
            var order = task.PageId is { } pid ? pageNumbers.GetValueOrDefault(pid) : int.MaxValue;
            var lot = task.LotId is { } lid ? lots.GetValueOrDefault(lid) : null;
            var submission = task.CurrentSubmissionId is { } sid ? pending.GetValueOrDefault(sid) : null;
            if (lot?.AssigneeUserId == cmd.UserId || submission?.SubmittedByUserId == cmd.UserId)
                blocked.Add((order, $"{Label(task)}: không được tự duyệt ô mình làm (BR-06).", ResultErrorCodes.Forbidden));
            else if (task.Status == StageTaskStatus.UnderReview && task.ReviewerUserId is { } reviewer && reviewer != cmd.UserId)
                blocked.Add((order, $"{Label(task)}: đang do người khác duyệt.", ResultErrorCodes.Conflict));
            else if (task.IsInputOutdated)
                blocked.Add((order, $"{Label(task)}: đầu vào đã lỗi thời, cần xử lý Impact Review trước.", ResultErrorCodes.Conflict));
            else if (task.Status is not (StageTaskStatus.Submitted or StageTaskStatus.UnderReview))
                blocked.Add((order, $"{Label(task)}: đang {task.Status}, chưa có bài chờ duyệt.", ResultErrorCodes.Conflict));
        }
        if (blocked.Count > 0)
            return Fail(string.Join("\n", blocked.OrderBy(b => b.Order).Select(b => b.Message)),
                blocked.Any(b => b.Code == ResultErrorCodes.Forbidden) ? ResultErrorCodes.Forbidden : ResultErrorCodes.Conflict);

        foreach (var task in tasks)
        {
            try
            {
                if (task.Status == StageTaskStatus.Submitted) task.OpenReview(cmd.UserId);
                task.Approve();
            }
            catch (InvalidOperationException ex) { return Fail($"{Label(task)}: {ex.Message}", ResultErrorCodes.Conflict); }
            task.UpdatedBy = cmd.UserId.ToString();
            if (task.CurrentSubmissionId is { } sid && pending.GetValueOrDefault(sid) is { } submission) submission.Approve(cmd.UserId);
        }

        // Một thông báo cho mỗi (người làm, công đoạn) thay vì mỗi ô
        foreach (var group in tasks.Where(t => t.LotId is { } lid && lots.ContainsKey(lid))
                     .GroupBy(t => (lots[t.LotId!.Value].AssigneeUserId, StageName(t))))
            _db.Notifications.Add(TaskGridNotifications.Approved(group.Key.AssigneeUserId, chapter.Id,
                group.Count() == 1 ? group.Key.Item2 : $"{group.Key.Item2} ({group.Count()} ô)"));

        await TaskGridNotifications.AddReadyAsync(_db, tasks, stages, chapterTasks, chapter.Id, ct);
        await TaskGridAutomation.AssignNextStagesAsync(_db, chapter, tasks, stages, chapterTasks, ct);
        await TaskGridRules.RefreshLotsAsync(_db, tasks.Select(t => t.LotId), tasks, ct);
        await _db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<StageTaskDto>>.Success(await TaskGridRules.WithSummaryAsync(_db,
            tasks.Select(t => StageTaskDto.From(t, t.LotId is { } lid ? lots.GetValueOrDefault(lid)?.AssigneeUserId : null, isStartable: false)), ct));
    }
}