using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Common;

/// <summary>
/// Luật tiến độ theo Panel dùng chung cho Start/Submit Assignment (CF2, BR-19, BR-20).
/// StageProgress là nguồn sự thật; CurrentStageId của Panel/Page chỉ là giá trị suy ra.
/// </summary>
internal static class StageProgressRules
{
    /// <summary>Các Panel mà Assignment bao phủ. Assignment cấp Chapter (vd stage Script) không có Panel.</summary>
    public static async Task<List<Panel>> ResolvePanelsAsync(IPanelForgeDbContext db, Assignment a, CancellationToken ct) =>
        a.EntityType switch
        {
            WorkflowEntityType.Panel => await db.Panels.Where(p => p.Id == a.TargetEntityId).ToListAsync(ct),
            WorkflowEntityType.Page => await db.Panels.Where(p => p.PageId == a.TargetEntityId).ToListAsync(ct),
            _ => []
        };

    /// <summary>
    /// BR-19: mỗi Panel chỉ bắt đầu khi stage trước của chính nó đã Approved (hoặc việc cấp Chapter ở stage trước đã Approved,
    /// vd stage Script). Panel/Page khác không bị ảnh hưởng. Trả thông báo lỗi, null nếu được phép.
    /// </summary>
    public static async Task<string?> PriorStageBlockReasonAsync(
        IPanelForgeDbContext db, Assignment a, PipelineStage stage, IReadOnlyList<Panel> panels, CancellationToken ct)
    {
        if (stage.IsInitial || panels.Count == 0) return null;

        var prior = await db.PipelineStages.AsNoTracking().FirstOrDefaultAsync(
            s => s.PipelineDefinitionId == stage.PipelineDefinitionId && s.StageOrder == stage.StageOrder - 1, ct);
        if (prior is null) return null;

        var chapterLevelApproved = await db.Assignments.AnyAsync(x =>
            x.ChapterId == a.ChapterId && x.EntityType == WorkflowEntityType.Chapter &&
            x.PipelineStageId == prior.Id && x.Status == AssignmentStatus.Approved, ct);
        if (chapterLevelApproved) return null;

        var panelIds = panels.Select(p => p.Id).ToList();
        var approved = await db.StageProgresses.AsNoTracking()
            .Where(s => s.PipelineStageId == prior.Id && s.Status == StageProgressStatus.Approved && panelIds.Contains(s.PanelId))
            .Select(s => s.PanelId).ToListAsync(ct);

        var missing = panelIds.Except(approved).Count();
        return missing == 0
            ? null
            : $"Stage trước ('{prior.Name}') chưa được duyệt cho {missing} panel, chưa thể bắt đầu stage '{stage.Name}'.";
    }

    /// <summary>
    /// Điều kiện nộp theo artifact [Đề xuất, design-research C.2]: Writer cần chapter có dòng script, Artist cần có Panel.
    /// Chưa kiểm được: hash artwork (chưa có upload), thoại đã gắn balloon (nằm ở event store). Trả lỗi hoặc null.
    /// </summary>
    public static async Task<string?> SubmissionBlockReasonAsync(
        IPanelForgeDbContext db, Assignment a, IReadOnlyList<Panel> panels, CancellationToken ct)
    {
        if (a.AssignedRole == WorkspaceRole.Writer)
        {
            var sceneIds = await db.Scenes.AsNoTracking().Where(s => s.ChapterId == a.ChapterId).Select(s => s.Id).ToListAsync(ct);
            var hasLines = await db.ScriptLines.AsNoTracking().AnyAsync(l => sceneIds.Contains(l.SceneId), ct);
            if (!hasLines) return "Chapter chưa có dòng script nào để nộp.";
        }
        else if (a.AssignedRole == WorkspaceRole.Artist && a.EntityType != WorkflowEntityType.Chapter)
        {
            if (panels.Count == 0) return "Chưa có Panel (layout) nào để nộp.";

            // CF2 mục "Điều kiện nộp": artwork đã tải lên. Sau khi bị trả về, phải có bản MỚI hơn lần từ chối (BR-01).
            var pageIds = panels.Select(p => p.PageId).Distinct().ToList();
            var rejectedAt = await db.Submissions.AsNoTracking()
                .Where(s => s.AssignmentId == a.Id && s.Status == SubmissionStatus.Rejected)
                .OrderByDescending(s => s.AttemptNumber)
                .Select(s => s.DecidedAt).FirstOrDefaultAsync(ct);

            var pagesWithArtwork = await db.AssetReferences.AsNoTracking()
                .Where(r => r.PipelineStageId == a.PipelineStageId && pageIds.Contains(r.PageId) &&
                            (rejectedAt == null || r.CreatedAt >= rejectedAt))
                .Select(r => r.PageId).Distinct().ToListAsync(ct);

            var missing = pageIds.Except(pagesWithArtwork).Count();
            if (missing > 0)
                return $"{missing} trang chưa có artwork ở stage này (sau lần bị trả về cần bản tải lên mới).";
        }

        return null;
    }

    /// <summary>
    /// Cổng nộp Lettering (BR-05, BR-20): mọi Page việc bao phủ không được còn chữ lỗi thời so với script, và mọi cảnh báo
    /// tràn chữ phải đã xác nhận kèm lý do. Thứ tự đọc / đè Key Artwork Region chỉ cảnh báo, không chặn. Trả lỗi hoặc null.
    /// </summary>
    public static async Task<string?> LetteringBlockReasonAsync(
        IPanelForgeDbContext db, IPageRepository pages, Assignment a, IReadOnlyList<Panel> panels, CancellationToken ct)
    {
        if (a.AssignedRole != WorkspaceRole.Letterer) return null;

        var pageIds = a.EntityType switch
        {
            WorkflowEntityType.Page => new List<Guid> { a.TargetEntityId },
            WorkflowEntityType.Panel => panels.Select(p => p.PageId).Distinct().ToList(),
            _ => await db.Pages.AsNoTracking().Where(p => p.ChapterId == a.ChapterId).Select(p => p.Id).ToListAsync(ct)
        };

        foreach (var pageId in pageIds)
        {
            var page = await pages.GetAsync(pageId, ct);
            if (page is null) continue;

            var analysis = await LetteringPageAnalysis.AnalyzeAsync(db, page, ct);
            if (analysis.SubmissionBlockReason(page.PageNumber) is { } reason) return reason;
        }

        return null;
    }

    /// <summary>
    /// CF3: áp một chuyển trạng thái (OpenReview/Approve/RequestChanges) lên StageProgress của các Panel được giao ở stage của việc.
    /// Panel chưa có dòng (thêm sau khi nộp) bị bỏ qua nên stage kế tiếp vẫn bị chặn cho Panel đó. Trả lỗi nếu có dòng sai trạng thái.
    /// </summary>
    public static async Task<string?> TransitionAsync(
        IPanelForgeDbContext db, Assignment a, Action<StageProgress> transition, CancellationToken ct)
    {
        var panels = await ResolvePanelsAsync(db, a, ct);
        if (panels.Count == 0) return null;

        var panelIds = panels.Select(p => p.Id).ToList();
        var rows = await db.StageProgresses
            .Where(s => s.PipelineStageId == a.PipelineStageId && panelIds.Contains(s.PanelId)).ToListAsync(ct);

        try
        {
            foreach (var row in rows) transition(row);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        return null;
    }

    /// <summary>
    /// Upsert StageProgress của các Panel được giao (start = InProgress, submit = Submitted) rồi suy ra lại
    /// CurrentStageId: Panel = stage cao nhất đã chạm tới; Page = stage thấp nhất trong các Panel của nó.
    /// Trả thông báo lỗi nếu có Panel không ở trạng thái cho phép (chưa Save), null nếu ổn.
    /// </summary>
    public static async Task<string?> ApplyAsync(
        IPanelForgeDbContext db, PipelineStage stage, IReadOnlyList<Panel> targetPanels, Guid userId, bool submit, CancellationToken ct)
    {
        if (targetPanels.Count == 0) return null;

        var pageIds = targetPanels.Select(p => p.PageId).Distinct().ToList();
        var pagePanels = await db.Panels.Where(p => pageIds.Contains(p.PageId)).ToListAsync(ct);
        var pagePanelIds = pagePanels.Select(p => p.Id).ToList();
        var rows = await db.StageProgresses.Where(s => pagePanelIds.Contains(s.PanelId)).ToListAsync(ct);

        try
        {
            foreach (var panel in targetPanels)
            {
                var row = rows.FirstOrDefault(r => r.PanelId == panel.Id && r.PipelineStageId == stage.Id);
                if (row is null)
                {
                    row = StageProgress.Begin(panel.Id, stage.Id);
                    db.StageProgresses.Add(row);
                    rows.Add(row);
                }
                else if (!submit)
                {
                    row.Resume();
                }

                if (submit) row.Submit(userId);
            }
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        var order = (await db.PipelineStages.AsNoTracking()
                .Where(s => s.PipelineDefinitionId == stage.PipelineDefinitionId)
                .Select(s => new { s.Id, s.StageOrder }).ToListAsync(ct))
            .ToDictionary(s => s.Id, s => s.StageOrder);

        foreach (var panel in pagePanels)
        {
            var current = rows.Where(r => r.PanelId == panel.Id)
                .OrderByDescending(r => order.GetValueOrDefault(r.PipelineStageId)).FirstOrDefault();
            panel.AdvanceToStage(current?.PipelineStageId);
        }

        var pages = await db.Pages.Where(p => pageIds.Contains(p.Id)).ToListAsync(ct);
        foreach (var page in pages)
        {
            var lowest = pagePanels.Where(p => p.PageId == page.Id && p.CurrentStageId != null)
                .OrderBy(p => order.GetValueOrDefault(p.CurrentStageId!.Value)).FirstOrDefault();
            page.AdvanceToStage(lowest?.CurrentStageId);
        }

        return null;
    }
}
