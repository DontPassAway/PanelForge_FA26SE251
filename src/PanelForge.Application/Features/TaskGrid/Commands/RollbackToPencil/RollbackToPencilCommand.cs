using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Commands.RollbackToPencil;

/// <summary>
/// BR-27.1 Self-Rollback (Module 2 mục 3): Artist giữ ô Pencil đã xong của một trang tự lùi về sửa Pencil, khi trang chưa vào
/// duyệt (không ô hạ nguồn nào đang UnderReview hoặc đã được Editor duyệt). Pencil về ReworkRequired; ô nhánh tranh của chính
/// người đó chưa nộp quay về chờ; ô còn lại của nhánh tranh bị gắn cờ đầu vào lỗi thời như khi Producer mở lại. Nhánh chữ không
/// bị chặn (vị trí bóng thoại độc lập), chỉ được nhắc kiểm tra lại bóng thoại.
/// </summary>
public sealed record RollbackToPencilCommand(Guid ChapterId, Guid TaskId, Guid UserId, string Reason)
    : IRequest<Result<ReopenStageTaskResultDto>>, ITransactionalCommand;

public sealed class RollbackToPencilCommandHandler : IRequestHandler<RollbackToPencilCommand, Result<ReopenStageTaskResultDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;

    public RollbackToPencilCommandHandler(IPanelForgeDbContext db, IPageRepository pages)
    {
        _db = db;
        _pages = pages;
    }

    public async Task<Result<ReopenStageTaskResultDto>> Handle(RollbackToPencilCommand cmd, CancellationToken ct)
    {
        static Result<ReopenStageTaskResultDto> Fail(string msg, string code = ResultErrorCodes.Validation) =>
            Result<ReopenStageTaskResultDto>.Failure(msg, code);

        if (string.IsNullOrWhiteSpace(cmd.Reason)) return Fail("Cần nêu lý do quay lại sửa Pencil.");

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var chapterTasks = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);
        var pencil = chapterTasks.FirstOrDefault(t => t.Id == cmd.TaskId);
        if (pencil is null) return Fail($"Ô {cmd.TaskId} không thuộc chương này.", ResultErrorCodes.NotFound);

        var stages = await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct);
        var stage = stages.FirstOrDefault(s => s.Id == pencil.PipelineStageId);
        if (stage is null || pencil.PageId is null || stage.AllowedRole != WorkspaceRole.Artist
            || TaskGridRules.IsLaterArtStage(stage, stages))
            return Fail("Chỉ quay lại được công đoạn vẽ đầu tiên (Pencil) của một trang.", ResultErrorCodes.Conflict);

        var lots = await _db.StageTaskLots.AsNoTracking().Where(l => l.ChapterId == chapter.Id)
            .ToDictionaryAsync(l => l.Id, l => l.AssigneeUserId, ct);
        Guid? HolderOf(StageTask t) => t.LotId is { } id && lots.TryGetValue(id, out var u) ? u : null;
        if (HolderOf(pencil) != cmd.UserId)
            return Fail("Chỉ Artist giữ ô Pencil của trang mới quay lại sửa được.", ResultErrorCodes.Forbidden);

        var downstream = TaskGridRules.Downstream(pencil, stages, chapterTasks);
        var stageById = stages.ToDictionary(s => s.Id);
        if (downstream.Any(t => t.Status == StageTaskStatus.UnderReview
                                || (t.Status == StageTaskStatus.Approved && stageById[t.PipelineStageId].IsApprovalGate)))
            return Fail("Trang đã vào duyệt (Gate 2); nhờ Editor trả bài hoặc Producer mở lại thay vì tự quay lại.", ResultErrorCodes.Conflict);

        var reason = cmd.Reason.Trim();
        try { pencil.Reopen(reason); }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }
        pencil.UpdatedBy = cmd.UserId.ToString();

        var outdated = new List<Guid>();
        var letterers = new HashSet<Guid>();
        var message = $"{stage.Name} trang này được Artist mở lại để sửa: {reason}";
        foreach (var cell in downstream)
        {
            if (IsLettering(stageById[cell.PipelineStageId]))
            {
                if (HolderOf(cell) is { } letterer && letterer != cmd.UserId) letterers.Add(letterer);
                continue;
            }

            cell.ClearReady();
            if (HolderOf(cell) == cmd.UserId && !cell.IsInputOutdated
                && cell.Status is StageTaskStatus.Assigned or StageTaskStatus.InProgress or StageTaskStatus.ReworkRequired)
                cell.ReturnToWaiting();
            else
            {
                var was = cell.IsInputOutdated;
                cell.MarkInputOutdated(message);
                if (cell.IsInputOutdated && !was) outdated.Add(cell.Id);
            }
            cell.UpdatedBy = cmd.UserId.ToString();
        }

        foreach (var group in chapterTasks.Where(t => outdated.Contains(t.Id))
                     .GroupBy(t => HolderOf(t)).Where(g => g.Key is { } u && u != cmd.UserId))
            _db.Notifications.Add(TaskGridNotifications.InputOutdated(group.Key!.Value, chapter.Id, group.Count(), message));
        // BR-27.1: trang đã đặt chữ thì nhắc người làm chữ kiểm tra lại vị trí bóng thoại sau khi Pencil đổi
        foreach (var letterer in letterers)
            _db.Notifications.Add(Notification.Create(letterer, NotificationType.AssignmentUpdated,
                "Kiểm tra lại vị trí bóng thoại", message, pencil.Id, chapter.Id));

        // ART-06: Pencil sẽ đổi nên bóng thoại đã đặt trên trang cần được kiểm tra lại
        await BalloonFitRules.FlagAsync(_db, _pages, pencil.PageId.Value, null, ct);

        await TaskGridRules.RefreshLotsAsync(_db, chapterTasks.Where(t => t.Id == pencil.Id || downstream.Contains(t)).Select(t => t.LotId),
            [pencil, .. downstream], ct);
        await _db.SaveChangesAsync(ct);

        return Result<ReopenStageTaskResultDto>.Success(new(
            (await TaskGridRules.WithSummaryAsync(_db, [StageTaskDto.From(pencil, cmd.UserId, isStartable: false)], ct))[0], outdated));
    }

    private static bool IsLettering(PipelineStage s) =>
        s.AllowedRole == WorkspaceRole.Letterer || s.Slug is "letter" or "lettering";
}
