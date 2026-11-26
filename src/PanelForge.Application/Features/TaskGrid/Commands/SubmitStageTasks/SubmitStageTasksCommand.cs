using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Commands.SubmitStageTasks;

/// <summary>
/// Người nhận nộp một danh sách ô (không khoá theo cả lô), tất cả hoặc không. Công đoạn có cổng duyệt → Submitted,
/// không có → tự duyệt. Mỗi ô ghi một <see cref="StageTaskSubmission"/> chụp các AssetReference mới nhất của ô (BR-01).
/// </summary>
public sealed record SubmitStageTasksCommand(Guid ChapterId, IReadOnlyList<Guid> TaskIds, Guid UserId)
    : IRequest<Result<IReadOnlyList<StageTaskDto>>>, ITransactionalCommand;

public sealed class SubmitStageTasksCommandHandler : IRequestHandler<SubmitStageTasksCommand, Result<IReadOnlyList<StageTaskDto>>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;

    public SubmitStageTasksCommandHandler(IPanelForgeDbContext db, IPageRepository pages)
    {
        _db = db;
        _pages = pages;
    }

    public async Task<Result<IReadOnlyList<StageTaskDto>>> Handle(SubmitStageTasksCommand cmd, CancellationToken ct)
    {
        static Result<IReadOnlyList<StageTaskDto>> Fail(string msg, string code = ResultErrorCodes.Validation) =>
            Result<IReadOnlyList<StageTaskDto>>.Failure(msg, code);

        var ids = cmd.TaskIds.Distinct().ToList();
        if (ids.Count == 0) return Fail("Cần ít nhất một ô để nộp.");

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked) return Fail(locked, ResultErrorCodes.Conflict);

        await _db.LockChapterAsync(chapter.Id, ct);

        var chapterTasks = await _db.StageTasks.Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);
        var tasks = chapterTasks.Where(t => ids.Contains(t.Id)).ToList();
        if (tasks.Count != ids.Count) return Fail("Có ô không thuộc chương này.", ResultErrorCodes.NotFound);

        var assigneeByLot = await TaskGridNotifications.AssigneesByLotAsync(_db, tasks, ct);
        if (tasks.Any(t => t.LotId is not { } lotId || assigneeByLot.GetValueOrDefault(lotId) != cmd.UserId))
            return Fail("Chỉ nộp được ô mình được giao.", ResultErrorCodes.Forbidden);

        var stages = await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct);
        var gateByStage = stages.ToDictionary(s => s.Id, s => s.IsApprovalGate);

        // Ảnh chụp lúc nộp: ô cấp Trang lấy trang của nó, ô cấp Chương lấy mọi trang còn sống của chương
        var chapterPageIds = await _db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id).Select(p => p.Id).ToListAsync(ct);
        var chapterPanels = await _db.Panels.AsNoTracking().Where(p => chapterPageIds.Contains(p.PageId)).ToListAsync(ct);
        var alivePanelIds = chapterPanels.Select(p => p.Id).ToList();
        var taskIds = tasks.Select(t => t.Id).ToList();

        // Điều kiện nộp như V2 (CF2, BR-05, BR-20): ô cấp Trang của Artist cần panel + artwork đủ, của Letterer cần chữ hợp lệ.
        var lotIds = tasks.Select(t => t.LotId!.Value).Distinct().ToList();
        var roleByLot = await _db.StageTaskLots.AsNoTracking().Where(l => lotIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.AssignedRole, ct);
        var blocked = new List<(int Page, string Reason)>();
        foreach (var task in tasks.Where(t => t.PageId != null))
        {
            var pageNumber = await _db.Pages.AsNoTracking().Where(p => p.Id == task.PageId).Select(p => p.PageNumber).FirstOrDefaultAsync(ct);
            var rejectedAt = await _db.StageTaskSubmissions.AsNoTracking()
                .Where(s => s.StageTaskId == task.Id && s.Status == SubmissionStatus.Rejected)
                .OrderByDescending(s => s.AttemptNumber).Select(s => s.DecidedAt).FirstOrDefaultAsync(ct);
            var reason = roleByLot.GetValueOrDefault(task.LotId!.Value) switch
            {
                WorkspaceRole.Artist => await StageProgressRules.ArtworkBlockReasonAsync(
                    _db, task.PipelineStageId, chapterPanels.Where(p => p.PageId == task.PageId).ToList(), rejectedAt, ct),
                WorkspaceRole.Letterer => await StageProgressRules.LetteringPagesBlockReasonAsync(_db, _pages, [task.PageId!.Value], ct),
                _ => null
            };
            if (reason is not null) blocked.Add((pageNumber, reason));
        }
        // Tất cả hoặc không, nhưng báo đủ mọi trang chưa đạt để người làm sửa một lượt
        if (blocked.Count > 0)
            return Fail(string.Join("\n", blocked.OrderBy(b => b.Page).Select(b => $"Trang {b.Page}: {b.Reason}")),
                ResultErrorCodes.Conflict);
        var attempts = await _db.StageTaskSubmissions.AsNoTracking().Where(s => taskIds.Contains(s.StageTaskId))
            .GroupBy(s => s.StageTaskId).Select(g => new { g.Key, Max = g.Max(s => s.AttemptNumber) })
            .ToDictionaryAsync(x => x.Key, x => x.Max, ct);
        // BR-01: lần nộp phải dựng lại được: bố cục Panel, version canvas (lớp chữ) và bản kịch bản chương đã commit
        // Bản đã đóng băng (Gate 1) là bản người làm vẽ theo; bản mới nhất có thể là nháp Writer đang sửa sau khi mở băng
        var scriptVersionId = chapter.FrozenScriptVersionId ?? await _db.ScriptVersions.AsNoTracking()
            .Where(v => v.ChapterId == chapter.Id)
            .OrderByDescending(v => v.VersionNumber).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct);
        var canvasVersionByPage = new Dictionary<Guid, long>();

        var autoApproved = new List<StageTask>();
        foreach (var task in tasks)
        {
            var gated = gateByStage.GetValueOrDefault(task.PipelineStageId);
            List<Guid> taskPages = task.PageId is { } pageId ? [pageId] : chapterPageIds;
            var artwork = await ChangeRequestRules.LatestArtworkIdsAsync(_db, task.PipelineStageId, taskPages, alivePanelIds, ct);
            foreach (var page in taskPages.Where(p => !canvasVersionByPage.ContainsKey(p)).ToList())
                canvasVersionByPage[page] = await _pages.GetStreamVersionAsync(page, ct);
            var submission = StageTaskSubmission.Create(task.Id, chapter.Id, cmd.UserId,
                attempts.GetValueOrDefault(task.Id) + 1, artwork, autoApproved: !gated,
                panelLayoutJson: SubmissionPanelDto.Serialize(chapterPanels.Where(p => taskPages.Contains(p.PageId))),
                canvasVersionsJson: JsonSerializer.Serialize(taskPages.ToDictionary(p => p, p => canvasVersionByPage[p])),
                scriptVersionId: scriptVersionId);
            try { task.Submit(submission.Id, gated); }
            catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

            submission.CreatedBy = task.UpdatedBy = cmd.UserId.ToString();
            _db.StageTaskSubmissions.Add(submission);
            if (!gated) autoApproved.Add(task);
        }

        if (autoApproved.Count > 0)
            await TaskGridNotifications.AddReadyAsync(_db, autoApproved, stages, chapterTasks, chapter.Id, ct);

        // Có ô cần duyệt: báo Editor (không có Editor thì Producer), trừ chính người nộp; một thông báo cho cả lần nộp
        var waitingCells = tasks.Where(t => t.Status == StageTaskStatus.Submitted).ToList();
        if (waitingCells.Count > 0)
        {
            var workspaceId = await _db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
            var routeReview = await _db.WorkspaceAutomationSettings.AsNoTracking()
                .AnyAsync(s => s.WorkspaceId == workspaceId && s.AutoRouteReview, ct);
            var ownerId = routeReview
                ? await _db.StudioWorkspaces.AsNoTracking().Where(w => w.Id == workspaceId).Select(w => w.OwnerId).FirstOrDefaultAsync(ct)
                : Guid.Empty;
            // R2 bật: mỗi ô một người được gợi ý, chỉ báo người đó (gom theo người nhận). Không có ai thì báo như cũ.
            if (routeReview && await TaskGridRules.SuggestReviewersAsync(_db, workspaceId, ownerId, cmd.UserId, waitingCells, ct))
                foreach (var group in waitingCells.GroupBy(t => t.ReviewerUserId!.Value))
                    _db.Notifications.Add(TaskGridNotifications.Submitted(group.Key, chapter.Id, chapter.ChapterNumber, group.Count()));
            else
                foreach (var reviewerId in await AssignmentRules.ReviewerUserIdsAsync(_db, workspaceId, cmd.UserId, ct))
                    _db.Notifications.Add(TaskGridNotifications.Submitted(reviewerId, chapter.Id, chapter.ChapterNumber, waitingCells.Count));
        }
        await TaskGridRules.RefreshLotsAsync(_db, tasks.Select(t => t.LotId), tasks, ct);
        await _db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<StageTaskDto>>.Success(
            await TaskGridRules.WithSummaryAsync(_db, tasks.Select(t => StageTaskDto.From(t, cmd.UserId, isStartable: false)), ct));
    }
}
