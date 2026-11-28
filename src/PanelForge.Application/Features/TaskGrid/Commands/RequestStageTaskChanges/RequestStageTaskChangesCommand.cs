using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Services;

namespace PanelForge.Application.Features.TaskGrid.Commands.RequestStageTaskChanges;

/// <summary>
/// Editor/Producer trả ô đã nộp về ReworkRequired kèm nhận xét bắt buộc (BR-06: khác người làm; ô đang do người khác duyệt thì không chồng).
/// REV-01/02 (Module 3 mục 1): <see cref="Changes"/> là các vùng khoanh trên trang. Mỗi vùng sinh một ChangeRequest cho từng khung giao
/// thoa ≥ 15% (chung GroupId), gửi tới ô đúng lớp lỗi của trang: lớp Tranh → ô vẽ cuối nhánh tranh, lớp Chữ → ô làm chữ. Ô nhận đang chờ
/// duyệt (hoặc đã tự duyệt) cũng bị trả về. Lỗi lớp Tranh trên khung có bóng thoại bật cờ Check Balloon Fit (REV-03).
/// </summary>
public sealed record RequestStageTaskChangesCommand(
    Guid ChapterId, Guid TaskId, Guid UserId, string Comment, IReadOnlyList<ChangeRequestItemRequest>? Changes = null)
    : IRequest<Result<StageTaskDto>>, ITransactionalCommand;

public sealed class RequestStageTaskChangesCommandHandler : IRequestHandler<RequestStageTaskChangesCommand, Result<StageTaskDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;

    public RequestStageTaskChangesCommandHandler(IPanelForgeDbContext db, IPageRepository pages)
    {
        _db = db;
        _pages = pages;
    }

    public async Task<Result<StageTaskDto>> Handle(RequestStageTaskChangesCommand cmd, CancellationToken ct)
    {
        static Result<StageTaskDto> Fail(string msg, string code) => Result<StageTaskDto>.Failure(msg, code);

        if (string.IsNullOrWhiteSpace(cmd.Comment)) return Fail("Trả về phải kèm nhận xét (BR-06).", ResultErrorCodes.Validation);
        if (ChangeRequestRules.ValidateItems(cmd.Changes) is { } itemError) return Fail(itemError, ResultErrorCodes.Validation);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var chapterTasks = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);
        var task = chapterTasks.FirstOrDefault(t => t.Id == cmd.TaskId);
        if (task is null) return Fail($"Ô {cmd.TaskId} không thuộc chương này.", ResultErrorCodes.NotFound);
        if (cmd.Changes is { Count: > 0 } && task.PageId is null)
            return Fail("Vùng khoanh chỉ dùng cho ô cấp trang.", ResultErrorCodes.Validation);

        var lots = await _db.StageTaskLots.AsNoTracking().Where(l => l.ChapterId == chapter.Id)
            .ToDictionaryAsync(l => l.Id, l => l.AssigneeUserId, ct);
        Guid? HolderOf(StageTask t) => t.LotId is { } id && lots.TryGetValue(id, out var u) ? u : null;

        var stages = await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct);
        var stageById = stages.ToDictionary(s => s.Id);
        var comment = cmd.Comment.Trim();
        var returned = new List<StageTask>();

        if (await ReturnAsync(task) is { } error) return error;

        // ── Vùng khoanh → ChangeRequest theo khung, đúng lớp lỗi ──
        if (cmd.Changes is { Count: > 0 } items)
        {
            var pageId = task.PageId!.Value;
            var panels = await _db.Panels.AsNoTracking().Where(p => p.PageId == pageId)
                .Select(p => new { p.Id, p.BoundingBox }).ToListAsync(ct);
            foreach (var item in items)
            {
                var target = TargetFor(item.Layer) ?? task;
                if (target != task && await ReturnAsync(target) is { } targetError) return targetError;
                var responsible = HolderOf(target) ?? HolderOf(task);
                if (responsible is null) return Fail("Ô nhận yêu cầu sửa chưa có người làm.", ResultErrorCodes.Conflict);

                var panelIds = item.PanelId is { } given
                    ? [given]
                    : string.IsNullOrWhiteSpace(item.RegionJson)
                        ? []
                        : PanelOverlap.FindIntersectingPanelIds(item.RegionJson, panels.Select(p => (p.Id, p.BoundingBox)));
                var groupId = panelIds.Count > 1 ? Guid.NewGuid() : item.GroupId;
                foreach (var panelId in panelIds.Count > 0 ? panelIds.Cast<Guid?>() : [null])
                {
                    _db.ChangeRequests.Add(ChangeRequest.CreateForStageTask(
                        target.Id, chapter.Id, responsible.Value, cmd.UserId, item.Description, item.RegionJson, panelId, groupId,
                        item.Layer, item.AssetReferenceId, item.DueDate));
                    if (item.Layer == ChangeRequestLayer.Artwork && panelId is { } flagged)
                        await BalloonFitRules.FlagAsync(_db, _pages, pageId, flagged, ct);
                }
            }
        }

        await TaskGridRules.RefreshLotsAsync(_db, returned.Select(c => c.LotId), returned, ct);
        await _db.SaveChangesAsync(ct);
        return Result<StageTaskDto>.Success((await TaskGridRules.WithSummaryAsync(
            _db, [StageTaskDto.From(task, HolderOf(task), isStartable: false)], ct))[0]);

        // Trả một ô về ReworkRequired (ô đang chờ duyệt, hoặc ô cuối nhánh kia đã tự duyệt); ô chưa nộp chỉ nhận phiếu sửa
        async Task<Result<StageTaskDto>?> ReturnAsync(StageTask cell)
        {
            var isMain = cell == task;
            if (!isMain && cell.Status == StageTaskStatus.Approved)
            {
                // Cuối nhánh song song chỉ ghép vào chính ô đang bị trả, nên không có ô hạ nguồn đã bắt đầu cần gắn cờ
                if (HolderOf(cell) == cmd.UserId) return Fail("Không được tự duyệt ô mình làm (BR-06).", ResultErrorCodes.Forbidden);
                try { cell.Reopen(comment); }
                catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }
                Notify(cell);
                return null;
            }
            if (!isMain && cell.Status is not (StageTaskStatus.Submitted or StageTaskStatus.UnderReview)) return null;

            // BR-06: người duyệt khác cả người đang giữ lô lẫn người đã nộp bản đang chờ (ô có thể đã đổi người sau khi nộp)
            var submission = await TaskGridRules.PendingSubmissionAsync(_db, cell, ct);
            if (HolderOf(cell) == cmd.UserId || submission?.SubmittedByUserId == cmd.UserId)
                return Fail("Không được tự duyệt ô mình làm (BR-06).", ResultErrorCodes.Forbidden);
            if (cell.Status == StageTaskStatus.UnderReview && cell.ReviewerUserId is { } reviewer && reviewer != cmd.UserId)
                return Fail("Ô này đang do người khác duyệt.", ResultErrorCodes.Conflict);
            try
            {
                if (cell.Status == StageTaskStatus.Submitted) cell.OpenReview(cmd.UserId);
                cell.RequestChanges(TaskGridRules.PrerequisitesApproved(cell, stages, chapterTasks));
            }
            catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

            submission?.Reject(cmd.UserId, comment);
            Notify(cell);
            return null;
        }

        void Notify(StageTask cell)
        {
            returned.Add(cell);
            cell.UpdatedBy = cmd.UserId.ToString();
            if (HolderOf(cell) is { } holder)
                _db.Notifications.Add(TaskGridNotifications.ChangesRequested(holder, chapter.Id,
                    stageById.GetValueOrDefault(cell.PipelineStageId)?.Name ?? "?", comment));
        }

        // Ô cùng trang chịu lớp lỗi: chính ô bị trả nếu đúng lớp, không thì bước cuối nhánh song song thuộc lớp đó (Gate 2 ghép)
        StageTask? TargetFor(ChangeRequestLayer layer)
        {
            if (!stageById.TryGetValue(task.PipelineStageId, out var stage)) return null;
            if (IsLettering(stage) == (layer == ChangeRequestLayer.Lettering)) return task;
            var sibling = StageDependencies.SiblingBranchEnds(stage, stages)
                .FirstOrDefault(s => IsLettering(s) == (layer == ChangeRequestLayer.Lettering));
            return sibling is null
                ? null
                : chapterTasks.FirstOrDefault(t => t.PipelineStageId == sibling.Id && t.PageId == task.PageId &&
                                                   t.Status != StageTaskStatus.Cancelled);
        }
    }

    private static bool IsLettering(PipelineStage s) =>
        s.AllowedRole == WorkspaceRole.Letterer || s.Slug is "letter" or "lettering";
}
