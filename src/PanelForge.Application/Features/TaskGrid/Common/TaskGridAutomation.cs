using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Queries.GetAutoAssignPlan;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Common;

/// <summary>
/// Assign Automation R1: công đoạn trước vừa được duyệt (Duyệt lẻ/lô, Nộp ở stage không cổng, Impact Review giữ) thì tự giao
/// ô cấp Trang hạ nguồn đang trống và đã đủ tiền đề. Chỉ tạo lô mới cho ô trống; không bao giờ đổi hay gỡ người.
/// Gọi sau <see cref="TaskGridNotifications.AddReadyAsync"/>, trong cùng khoá chương, với <c>chapterTasks</c> đã mang trạng thái mới.
/// </summary>
public static class TaskGridAutomation
{
    /// <summary>
    /// G3: sau khi duyệt/nộp, chương đang sản xuất không còn điều kiện chặn nào của <see cref="Chapters.Common.ChapterLockGate"/>
    /// thì nhắc Producer + Owner chốt chương (một lần). Không tự chốt. Chạy khi Studio bật auto_next_stage.
    /// Gọi SAU SaveChanges và trước commit: ChapterLockGate đếm thẳng từ DB nên phải thấy trạng thái vừa ghi. Tự lưu thông báo.
    /// </summary>
    public static async Task NudgeChapterLockAsync(IPanelForgeDbContext db, Chapter chapter, CancellationToken ct)
    {
        if (chapter.Status != ChapterStatus.InProduction) return;
        var workspaceId = await db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
        if (!await db.WorkspaceAutomationSettings.AsNoTracking().AnyAsync(s => s.WorkspaceId == workspaceId && s.AutoNextStage, ct)) return;
        if ((await Chapters.Common.ChapterLockGate.EvaluateAsync(db, chapter, ct)).Any(i => i.Blocking)) return;

        var ownerId = await db.StudioWorkspaces.AsNoTracking().Where(w => w.Id == workspaceId).Select(w => w.OwnerId).FirstOrDefaultAsync(ct);
        var producers = (await db.WorkspaceMembers.AsNoTracking()
                .Where(m => m.WorkspaceId == workspaceId && m.Status == WorkspaceMemberStatus.Active && m.Role == WorkspaceRole.Producer)
                .Select(m => m.UserId).ToListAsync(ct))
            .Append(ownerId).Where(id => id != Guid.Empty).Distinct().ToList();
        var added = false;
        foreach (var producer in producers)
        {
            var note = TaskGridNotifications.ChapterReadyToLock(producer, chapter.Id, chapter.ChapterNumber);
            if (await db.Notifications.AnyAsync(n => n.UserId == producer && n.ChapterId == chapter.Id && n.Message == note.Message, ct)) continue;
            db.Notifications.Add(note);
            added = true;
        }
        if (added) await db.SaveChangesAsync(ct);
    }

    public static async Task AssignNextStagesAsync(IPanelForgeDbContext db, Chapter chapter, IReadOnlyList<StageTask> approved,
        IReadOnlyList<PipelineStage> stages, IReadOnlyList<StageTask> chapterTasks, CancellationToken ct)
    {
        if (approved.Count == 0) return;
        var workspaceId = await db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
        var settings = await db.WorkspaceAutomationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, ct);
        if (settings is not { AutoNextStage: true }) return;

        // Ô trống cấp Trang, hạ nguồn trực tiếp của ô vừa duyệt (cùng trang), giờ đã đủ tiền đề. MVP: không làm stage cấp Chương.
        var approvedStageIds = approved.Select(t => t.PipelineStageId).ToHashSet();
        var approvedPages = approved.Select(t => t.PageId).ToHashSet();
        var stageById = stages.ToDictionary(s => s.Id);
        var cells = chapterTasks.Where(t => t.Status == StageTaskStatus.Unassigned && t.PageId is not null
                && stageById.TryGetValue(t.PipelineStageId, out var stage) && stage.IsEnabled
                && StageDependencies.DirectPrerequisites(stage, stages).Any(p => approvedStageIds.Contains(p.Id))
                && (approvedPages.Contains(t.PageId) || approvedPages.Contains(null))
                && TaskGridRules.PrerequisitesApproved(t, stages, chapterTasks))
            .ToList();
        if (cells.Count == 0) return;

        var ownerId = await db.StudioWorkspaces.AsNoTracking().Where(w => w.Id == workspaceId).Select(w => w.OwnerId).FirstOrDefaultAsync(ct);
        var members = await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Status == WorkspaceMemberStatus.Active)
            .ToDictionaryAsync(m => m.UserId, m => m.Role, ct);
        var names = await db.Users.AsNoTracking().Where(u => members.Keys.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var pageNumbers = await db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id).ToDictionaryAsync(p => p.Id, p => p.PageNumber, ct);
        var chapterLots = await db.StageTaskLots.AsNoTracking().Where(l => l.ChapterId == chapter.Id)
            .ToDictionaryAsync(l => l.Id, l => l.AssigneeUserId, ct);

        // Tải = panel chưa xong: ô chương khác đọc DB, ô chương này lấy trong bộ nhớ (đã mang trạng thái mới, không đếm trùng)
        var seriesIds = await db.Series.AsNoTracking().Where(s => s.WorkspaceId == workspaceId).Select(s => s.Id).ToListAsync(ct);
        var memberIds = members.Keys.ToList();
        var otherOpen = await (from t in db.StageTasks.AsNoTracking()
                               join l in db.StageTaskLots.AsNoTracking() on t.LotId equals (Guid?)l.Id
                               where t.ChapterId != chapter.Id && seriesIds.Contains(t.SeriesId) && memberIds.Contains(l.AssigneeUserId)
                                     && t.Status != StageTaskStatus.Approved && t.Status != StageTaskStatus.Cancelled
                               select new { l.AssigneeUserId, t.PageId }).ToListAsync(ct);
        var thisOpen = chapterTasks.Where(t => t.LotId is { } lid && chapterLots.ContainsKey(lid)
                && t.Status != StageTaskStatus.Approved && t.Status != StageTaskStatus.Cancelled)
            .Select(t => new { AssigneeUserId = chapterLots[t.LotId!.Value], t.PageId }).ToList();
        var weightedPages = otherOpen.Concat(thisOpen).Select(c => c.PageId).Concat(cells.Select(c => c.PageId)).OfType<Guid>().Distinct().ToList();
        var panelCounts = (await db.Panels.AsNoTracking().Where(p => weightedPages.Contains(p.PageId)).Select(p => p.PageId).ToListAsync(ct))
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        int Weight(Guid? pageId) => pageId is { } id ? Math.Max(1, panelCounts.GetValueOrDefault(id)) : 1;
        var load = memberIds.ToDictionary(id => id, _ => 0);
        foreach (var c in otherOpen.Concat(thisOpen).Where(c => load.ContainsKey(c.AssigneeUserId))) load[c.AssigneeUserId] += Weight(c.PageId);

        var generated = stages.Where(s => s.IsEnabled && !(s.IsTerminal && !s.IsApprovalGate)).ToList();
        var skipped = new List<string>();
        var now = DateTime.UtcNow;

        foreach (var stageCells in cells.GroupBy(c => c.PipelineStageId))
        {
            var stage = stageById[stageCells.Key];
            var ordered = stageCells.OrderBy(c => pageNumbers.GetValueOrDefault(c.PageId!.Value)).ToList();
            if (stage.AllowedRole is not { } role)
            {
                skipped.Add($"{ordered.Count} ô {stage.Name} để trống vì công đoạn chưa cấu hình vai trò.");
                continue;
            }
            var exact = members.Where(m => m.Value == role).Select(m => m.Key).ToList();

            var picks = new List<(StageTask Cell, Guid UserId, string Reason)>();
            var noOne = 0;
            foreach (var cell in ordered)
            {
                // Người làm trang này ở công đoạn tiền đề (Active, đủ vai trò giữ stage)
                var pageOwner = StageDependencies.DirectPrerequisites(stage, stages)
                    .SelectMany(p => chapterTasks.Where(t => t.PipelineStageId == p.Id && t.PageId == cell.PageId))
                    .Where(t => t.LotId is { } lid && chapterLots.ContainsKey(lid))
                    .Select(t => (Guid?)chapterLots[t.LotId!.Value])
                    .FirstOrDefault(id => id is { } u && members.TryGetValue(u, out var r) && stage.CanBeHeldBy(r));

                if (settings.AssignMode == AutoAssignMode.KeepPageOwner && pageOwner is { } keep && members[keep] == role)
                    picks.Add((cell, keep, "Giữ người đã làm trang này ở công đoạn trước"));
                else if (exact.Count > 0)
                {
                    var pick = exact.OrderBy(id => load[id]).ThenBy(id => id).First();
                    picks.Add((cell, pick, $"Cân tải: {names.GetValueOrDefault(pick, "thành viên")} đang ít panel chưa xong nhất"));
                }
                else if (pageOwner is { } artist)
                    picks.Add((cell, artist, $"Không có {role} Active: giao người đã làm trang này (BR-31)"));
                else
                {
                    noOne++;
                    continue;
                }
                load[picks[^1].UserId] += Weight(cell.PageId);
            }
            if (noOne > 0) skipped.Add($"{noOne} ô {stage.Name} để trống vì chưa có {role} Active.");

            var suggested = GetAutoAssignPlanQueryHandler.SuggestedDueDate(chapter.TargetReleaseDate, stage, generated);
            DateTime? byEstimate = stage.EstimatedDurationDays is { } days ? now.AddDays(days) : null;
            var due = suggested is { } s && s > now
                ? (byEstimate is { } e && e < s ? e : s)
                : byEstimate;

            // Gom trang liền nhau của cùng người thành một lô
            var runs = new List<List<(StageTask Cell, Guid UserId, string Reason)>>();
            foreach (var p in picks)
            {
                var last = runs.LastOrDefault();
                if (last is not null && last[^1].UserId == p.UserId
                    && pageNumbers.GetValueOrDefault(p.Cell.PageId!.Value) == pageNumbers.GetValueOrDefault(last[^1].Cell.PageId!.Value) + 1)
                    last.Add(p);
                else runs.Add([p]);
            }
            foreach (var run in runs)
            {
                var userId = run[0].UserId;
                var actualRole = members[userId];
                var from = pageNumbers.GetValueOrDefault(run[0].Cell.PageId!.Value);
                var to = pageNumbers.GetValueOrDefault(run[^1].Cell.PageId!.Value);
                var lot = StageTaskLot.Create(chapter.SeriesId, chapter.Id, stage.Id, userId, actualRole, ownerId,
                    from == to ? $"Tự động giao trang {from}" : $"Tự động giao trang {from}-{to}", due);
                lot.MarkAutoAssigned(run[0].Reason);
                lot.CreatedBy = ownerId.ToString();
                db.StageTaskLots.Add(lot);
                foreach (var (cell, _, _) in run) cell.AssignTo(lot.Id, prerequisitesApproved: true);
                db.Notifications.Add(TaskGridNotifications.Assigned(lot, run.Count, stage.Name));
                await AssignmentRules.EnsureSeriesMemberAsync(db, chapter.SeriesId, userId, actualRole, ct);
            }
        }

        if (skipped.Count == 0) return;
        var producers = (await db.WorkspaceMembers.AsNoTracking()
                .Where(m => m.WorkspaceId == workspaceId && m.Status == WorkspaceMemberStatus.Active && m.Role == WorkspaceRole.Producer)
                .Select(m => m.UserId).ToListAsync(ct))
            .Append(ownerId).Where(id => id != Guid.Empty).Distinct();
        var message = string.Join(" ", skipped);
        foreach (var producer in producers)
            db.Notifications.Add(TaskGridNotifications.AutoAssignSkipped(producer, chapter.Id, chapter.ChapterNumber, message));
    }
}