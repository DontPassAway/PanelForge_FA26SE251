using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assets.Common;

/// <summary>Ai được ghi một phiên bản artwork mới vào (Page, Panel, Stage): dùng chung cho upload và khôi phục bản cũ (ART-16).</summary>
internal static class ArtworkWriteRules
{
    /// <summary>
    /// Lý do bị từ chối (403) hoặc null nếu được ghi. Lưới V3: chương đã giao việc qua lưới thì xét theo ô (trang × công đoạn) —
    /// chỉ người đang làm ô (InProgress/ReworkRequired, đầu vào còn mới); ô chưa giao chỉ Producer. Chương chưa dùng lưới: luật V2
    /// theo Assignment (việc cấp Page: ảnh cả trang hoặc khung bất kỳ; việc cấp Panel: chỉ đúng khung đó), hoặc Owner/Producer/Artist
    /// của Series khi phần đó chưa giao cho ai.
    /// </summary>
    public static async Task<string?> DeniedReasonAsync(
        IPanelForgeDbContext db, Chapter chapter, Guid workspaceId, Guid stageId, Guid pageId, Guid? panelId, Guid userId,
        CancellationToken ct)
    {
        var usesGrid = await db.StageTasks.AnyAsync(
            t => t.ChapterId == chapter.Id && t.Status != StageTaskStatus.Cancelled, ct);
        if (usesGrid)
        {
            var cell = await (from t in db.StageTasks.AsNoTracking()
                              join l in db.StageTaskLots.AsNoTracking() on t.LotId equals (Guid?)l.Id
                              where t.ChapterId == chapter.Id && t.PipelineStageId == stageId && t.PageId == pageId &&
                                    t.Status != StageTaskStatus.Cancelled
                              select new { t.Status, t.IsInputOutdated, l.AssigneeUserId }).FirstOrDefaultAsync(ct);
            var holdsCell = cell is not null && cell.AssigneeUserId == userId && !cell.IsInputOutdated &&
                            (cell.Status == StageTaskStatus.InProgress || cell.Status == StageTaskStatus.ReworkRequired);
            var producerOnFreeCell = cell is null && await db.WorkspaceMembers.AsNoTracking().AnyAsync(m =>
                m.WorkspaceId == workspaceId && m.UserId == userId && m.Status == WorkspaceMemberStatus.Active &&
                m.Role == WorkspaceRole.Producer, ct);
            return holdsCell || producerOnFreeCell
                ? null
                : "Chỉ người đang làm ô (trang × công đoạn) này trên lưới giao việc mới tải được artwork.";
        }

        var holdsTheWork = false;
        if (stageId != Guid.Empty)
        {
            holdsTheWork = await db.Assignments.AnyAsync(a =>
                a.ChapterId == chapter.Id && a.PipelineStageId == stageId && a.AssigneeUserId == userId &&
                a.Status == AssignmentStatus.InProgress &&
                ((a.EntityType == WorkflowEntityType.Page && a.TargetEntityId == pageId) ||
                 (a.EntityType == WorkflowEntityType.Panel && panelId != null && a.TargetEntityId == panelId)), ct);

            if (!holdsTheWork && await db.Assignments.AnyAsync(a =>
                    a.ChapterId == chapter.Id && a.PipelineStageId == stageId && a.AssigneeUserId == userId &&
                    a.Status == AssignmentStatus.InProgress && a.EntityType == WorkflowEntityType.Chapter, ct))
            {
                var carved = await StageProgressRules.CarvedOutAsync(db, chapter.Id, stageId, ct);
                holdsTheWork = panelId is { } pid
                    ? !carved.Panels.Contains(pid)
                    : !carved.Pages.Contains(pageId) &&
                      !await db.Panels.AsNoTracking().AnyAsync(p => p.PageId == pageId && carved.Panels.Contains(p.Id), ct);
            }
        }

        // Owner / Producer Workspace / Artist-Producer của Series ghi trực tiếp khi stage này chưa giao cho ai ở phần đó.
        // Việc cả chương cũng tính là đã giao, nếu không thành viên khác ghi đè được stage người khác đang giữ.
        if (!holdsTheWork && userId != Guid.Empty)
        {
            var hasSpecificAssignment = await db.Assignments.AnyAsync(a =>
                a.ChapterId == chapter.Id && a.PipelineStageId == stageId &&
                (a.EntityType == WorkflowEntityType.Chapter ||
                 (a.EntityType == WorkflowEntityType.Page && a.TargetEntityId == pageId) ||
                 (a.EntityType == WorkflowEntityType.Panel && panelId != null && a.TargetEntityId == panelId)), ct);

            if (!hasSpecificAssignment)
            {
                holdsTheWork =
                    (db.StudioWorkspaces != null && await db.StudioWorkspaces.AsNoTracking().AnyAsync(w =>
                        w.Id == workspaceId && (w.OwnerId == userId || w.CreatedBy == userId.ToString()), ct))
                    || (db.WorkspaceMembers != null && await db.WorkspaceMembers.AsNoTracking().AnyAsync(m =>
                        m.WorkspaceId == workspaceId && m.UserId == userId && m.Status == WorkspaceMemberStatus.Active &&
                        m.Role == WorkspaceRole.Producer, ct)) // D10: Artist chỉ tính theo vai trò Series bên dưới
                    || (db.SeriesMembers != null && await db.SeriesMembers.AsNoTracking().AnyAsync(sm =>
                        sm.SeriesId == chapter.SeriesId && sm.UserId == userId &&
                        (sm.Role == WorkspaceRole.Artist || sm.Role == WorkspaceRole.Producer), ct));
            }
        }

        if (holdsTheWork) return null;
        return panelId is null
            ? "Chỉ người giữ việc cả trang (InProgress) ở stage này hoặc thành viên Artist/Producer mới được tải artwork cả trang."
            : "Chỉ người đang làm việc (InProgress) ở stage này cho Page/khung này hoặc thành viên Artist/Producer mới được tải artwork.";
    }

    /// <summary>Phiên bản kế tiếp của (Page, Panel, Stage) — chỉ thêm (BR-01); unique index chặn hai lần ghi trùng số.</summary>
    public static async Task<int> NextVersionAsync(
        IPanelForgeDbContext db, Guid pageId, Guid? panelId, Guid stageId, CancellationToken ct)
    {
        var versions = await db.AssetReferences.AsNoTracking()
            .Where(r => r.PageId == pageId && r.PanelId == panelId && r.PipelineStageId == stageId)
            .Select(r => r.VersionNumber).ToListAsync(ct);
        return versions.Count == 0 ? 1 : versions.Max() + 1;
    }

    /// <summary>ART-06: công đoạn vẽ sau Pencil (nét/màu) có thể làm lệch bóng thoại đã đặt → cần cờ Check Balloon Fit.</summary>
    public static async Task<bool> IsLaterArtStageAsync(IPanelForgeDbContext db, Guid seriesId, Guid stageId, CancellationToken ct)
    {
        if (stageId == Guid.Empty) return false;
        var stages = await TaskGridRules.LoadStagesAsync(db, seriesId, ct);
        var stage = stages.FirstOrDefault(s => s.Id == stageId);
        return stage is not null && TaskGridRules.IsLaterArtStage(stage, stages);
    }
}
