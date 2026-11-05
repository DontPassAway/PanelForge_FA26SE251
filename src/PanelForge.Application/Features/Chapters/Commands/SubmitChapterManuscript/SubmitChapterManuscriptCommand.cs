using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.SubmitChapterManuscript;

/// <summary>
/// Nộp bản thảo toàn bộ chương hoặc theo từng giai đoạn (Stage) trực tiếp từ Bàn vẽ Artist.
/// Tự động duyệt và kích hoạt bàn giao (Internal Handoff Pencil ➔ Ink, Ink ➔ Lettering) khi stage không bắt buộc Editor phê duyệt.
/// </summary>
public sealed record SubmitChapterManuscriptCommand(Guid ChapterId, Guid UserId, Guid? StageId = null)
    : IRequest<Result<AssignmentDto>>;

public sealed class SubmitChapterManuscriptCommandHandler : IRequestHandler<SubmitChapterManuscriptCommand, Result<AssignmentDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;

    public SubmitChapterManuscriptCommandHandler(IPanelForgeDbContext db, IPageRepository pages)
    {
        _db = db;
        _pages = pages;
    }

    public async Task<Result<AssignmentDto>> Handle(SubmitChapterManuscriptCommand cmd, CancellationToken ct)
    {
        static Result<AssignmentDto> Fail(string msg, string code = ResultErrorCodes.Validation) =>
            Result<AssignmentDto>.Failure(msg, code);

        var chapter = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (chapter.Status == ChapterStatus.Approved || chapter.Status == ChapterStatus.Published)
            return Fail("Chương này đã được duyệt hoặc xuất bản, không thể nộp lại.", ResultErrorCodes.Conflict);

        var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == chapter.SeriesId, ct);
        if (series is null)
            return Fail("Series không tồn tại.", ResultErrorCodes.NotFound);

        // Kiểm tra quyền: thành viên Workspace hoặc Studio Owner
        var isMember = await _db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == series.WorkspaceId && m.UserId == cmd.UserId, ct)
            || await _db.StudioWorkspaces.AnyAsync(w => w.Id == series.WorkspaceId && w.OwnerId == cmd.UserId, ct);
        if (!isMember)
            return Fail("Bạn không phải thành viên của studio này.", ResultErrorCodes.Forbidden);

        // Phải có ít nhất 1 trang tranh
        var hasPages = await _db.Pages.AnyAsync(p => p.ChapterId == chapter.Id, ct);
        if (!hasPages)
            return Fail("Chương chưa có trang nào để nộp bản thảo.", ResultErrorCodes.Conflict);

        // Xác định Stage đang nộp
        PipelineStage? targetStage = null;
        if (cmd.StageId.HasValue)
        {
            targetStage = await _db.PipelineStages.FirstOrDefaultAsync(s => s.Id == cmd.StageId.Value, ct);
        }

        if (targetStage is null)
        {
            var pipeline = await _db.PipelineDefinitions.AsNoTracking().FirstOrDefaultAsync(p => p.SeriesId == chapter.SeriesId, ct);
            if (pipeline != null)
            {
                targetStage = await _db.PipelineStages
                    .Where(s => s.PipelineDefinitionId == pipeline.Id && s.IsEnabled != false && s.AllowedRole == WorkspaceRole.Artist)
                    .OrderBy(s => s.StageOrder)
                    .FirstOrDefaultAsync(ct);
            }

            targetStage ??= await _db.PipelineStages
                .Where(s => s.IsEnabled != false)
                .OrderBy(s => s.StageOrder)
                .FirstOrDefaultAsync(ct);
        }

        var stageId = targetStage?.Id ?? Guid.NewGuid();
        var isAutoApproveStage = targetStage != null && !targetStage.IsApprovalGate;

        // 1. Tìm assignment tương ứng hoặc tạo mới
        var existingAssignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.ChapterId == cmd.ChapterId &&
                a.PipelineStageId == stageId &&
                (a.AssigneeUserId == cmd.UserId || a.EntityType == WorkflowEntityType.Chapter), ct);

        // (Fixed) Do not fallback to unrelated stage assignment to prevent false UnderReview locks

        Assignment assignment;
        if (existingAssignment is not null)
        {
            assignment = existingAssignment;
            if (assignment.Status == AssignmentStatus.Assigned)
            {
                try { assignment.StartWork(); } catch { }
            }
        }
        else
        {
            assignment = Assignment.Create(
                chapter.SeriesId,
                chapter.Id,
                WorkflowEntityType.Chapter,
                chapter.Id,
                stageId,
                cmd.UserId,
                targetStage?.AllowedRole ?? WorkspaceRole.Artist,
                $"Bản thảo {targetStage?.Name ?? "Vẽ"} Chương {chapter.ChapterNumber}",
                "Bản thảo nộp trực tiếp từ Bàn vẽ Artist",
                null,
                null,
                AssignmentPriority.Medium
            );
            try { assignment.StartWork(); } catch { }
            _db.Assignments.Add(assignment);
        }

        // Đánh dấu Submit / Auto-Approve (Internal Handoff: Pencil ➔ Ink không bắt Editor duyệt)
        try
        {
            if (isAutoApproveStage)
            {
                // Công đoạn nội bộ tự động duyệt (ví dụ Pencil ➔ Ink):
                // Bất kể đang InProgress, Assigned, Submitted hay UnderReview -> Chuyển thẳng sang Approved
                if (assignment.Status != AssignmentStatus.Approved)
                {
                    assignment.MarkApproved();
                }
            }
            else
            {
                // Công đoạn cần Editor duyệt (ví dụ Gate 2 Lettering ➔ Review):
                if (assignment.Status == AssignmentStatus.Assigned || 
                    assignment.Status == AssignmentStatus.InProgress || 
                    assignment.Status == AssignmentStatus.ReworkRequired)
                {
                    assignment.SubmitForReview();
                }
                // Nếu đã là Submitted hoặc UnderReview rồi thì coi như đang trong hàng chờ duyệt
            }
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message, ResultErrorCodes.Conflict);
        }

        // Cập nhật StageProgress cho các trang và khung tranh
        var pages = await _db.Pages.Where(p => p.ChapterId == chapter.Id).ToListAsync(ct);
        var pageIds = pages.Select(p => p.Id).ToList();
        var pagePanels = await _db.Panels.Where(p => pageIds.Contains(p.PageId)).ToListAsync(ct);

        if (targetStage is not null && pagePanels.Count > 0)
        {
            await StageProgressRules.ApplyAsync(_db, targetStage, pagePanels, cmd.UserId, submit: true, ct, autoApprove: isAutoApproveStage);
        }

        if (!isAutoApproveStage)
        {
            // Báo người duyệt (Editor / Producer) khi stage có ApprovalGate (Gate 2: Quality & Composite Gate)
            foreach (var reviewerId in await AssignmentRules.ReviewerUserIdsAsync(_db, series.WorkspaceId, cmd.UserId, ct))
            {
                var stageSlug = targetStage?.Slug?.ToLowerInvariant() ?? "";
                var isGate2 = stageSlug.Contains("letter") || stageSlug.Contains("review") || targetStage?.IsApprovalGate == true;
                var notifTitle = isGate2
                    ? "Gate 2 (Quality and Composite Gate): Cho tham dinh"
                    : "Co ban thao moi duoc nop cho duyet";
                var notifMsg = isGate2
                    ? $"Ban thao hoan thien Chuong {chapter.ChapterNumber} (net ve, bong thoai va SFX) da duoc nop de Editor kiem tra tong the do de doc, ngat dong tieng Viet va bo cuc trang."
                    : AssignmentNotifications.Describe(assignment);

                _db.Notifications.Add(Domain.Entities.Workflow.Notification.Create(
                    reviewerId,
                    NotificationType.AssignmentSubmitted,
                    notifTitle,
                    notifMsg,
                    assignment.Id,
                    chapter.Id));
            }
        }

        // Ghi lại submission
        var artworkIds = await ChangeRequestRules.LatestArtworkIdsAsync(
            _db, assignment.PipelineStageId, pageIds, pagePanels.Select(p => p.Id), ct);

        var canvasVersions = new Dictionary<Guid, long>();
        foreach (var pageId in pageIds)
        {
            try { canvasVersions[pageId] = await _pages.GetStreamVersionAsync(pageId, ct); }
            catch { canvasVersions[pageId] = 0; }
        }

        var sceneIds = await _db.Scenes.AsNoTracking().Where(s => s.ChapterId == chapter.Id).Select(s => s.Id).ToListAsync(ct);
        var scriptRevisionIds = (await _db.ContentRevisions.AsNoTracking()
                .Where(r => sceneIds.Contains(r.SceneId) && r.Kind == ContentRevisionKind.Script)
                .Select(r => new { r.Id, r.SceneId, r.VersionNumber }).ToListAsync(ct))
            .GroupBy(r => r.SceneId).Select(g => g.OrderByDescending(r => r.VersionNumber).First().Id).ToList();

        await ChangeRequestRules.RecordSubmissionAsync(
            _db, assignment.Id, chapter.Id, cmd.UserId, ct, artworkIds, chapter.VersionVector,
            SubmissionPanelDto.Serialize(pagePanels), JsonSerializer.Serialize(canvasVersions), scriptRevisionIds);

        if (isAutoApproveStage)
        {
            var pendingSub = await ChangeRequestRules.LatestPendingSubmissionAsync(_db, assignment.Id, ct);
            pendingSub?.Approve(cmd.UserId);
            _db.Notifications.Add(AssignmentNotifications.Approved(assignment));

            // Internal Handoff (Bàn giao tự động Pencil ➔ Ink, Ink ➔ Lettering)
            if (targetStage != null)
            {
                await HandleAutoHandoffAsync(_db, chapter, targetStage, assignment, ct);
            }
        }

        assignment.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<AssignmentDto>.Success(AssignmentDto.From(assignment));
    }

    private static async Task HandleAutoHandoffAsync(
        IPanelForgeDbContext db,
        Domain.Entities.Content.Chapter chapter,
        PipelineStage currentStage,
        Assignment completedAssignment,
        CancellationToken ct)
    {
        var nextStage = await db.PipelineStages
            .Where(s => s.PipelineDefinitionId == currentStage.PipelineDefinitionId 
                     && s.StageOrder > currentStage.StageOrder 
                     && s.IsEnabled)
            .OrderBy(s => s.StageOrder)
            .FirstOrDefaultAsync(ct);

        if (nextStage is null) return;

        var currentSlug = currentStage.Slug?.ToLowerInvariant() ?? "";
        var nextSlug = nextStage.Slug?.ToLowerInvariant() ?? "";

        // 1. Internal Handoff (Bàn giao tự động Pencil ➔ Ink):
        // Không bắt Editor duyệt nét chì. Khi Pencil nộp bài, hệ thống tự động gán mở việc cho Ink.
        // Nghệ sĩ có thể tự hoàn thiện liên tục mà không bị gián đoạn.
        if (currentSlug.Contains("pencil") || (nextSlug.Contains("ink") && currentStage.AllowedRole == WorkspaceRole.Artist))
        {
            var nextAssignment = await db.Assignments
                .FirstOrDefaultAsync(a => a.ChapterId == chapter.Id 
                                       && a.PipelineStageId == nextStage.Id 
                                       && (a.TargetEntityId == completedAssignment.TargetEntityId || a.EntityType == WorkflowEntityType.Chapter), ct);

            if (nextAssignment is not null)
            {
                if (nextAssignment.Status == AssignmentStatus.Assigned)
                {
                    try { nextAssignment.StartWork(); } catch { }
                }
                db.Notifications.Add(Domain.Entities.Workflow.Notification.Create(
                    nextAssignment.AssigneeUserId,
                    NotificationType.AssignmentAssigned,
                    "Bàn giao tự động Pencil ➔ Ink",
                    $"Công đoạn nét chì (Pencil) của Chương {chapter.ChapterNumber} đã hoàn thành. Hệ thống đã tự động mở việc Ink để bạn tiếp tục hoàn thiện liên tục.",
                    nextAssignment.Id,
                    chapter.Id));
            }
            else
            {
                var autoInk = Assignment.Create(
                    chapter.SeriesId,
                    chapter.Id,
                    completedAssignment.EntityType,
                    completedAssignment.TargetEntityId,
                    nextStage.Id,
                    completedAssignment.AssigneeUserId,
                    WorkspaceRole.Artist,
                    $"Đi nét (Ink) - {completedAssignment.Brief}",
                    "Bàn giao tự động từ hoàn thành công đoạn Pencil",
                    null,
                    DateTime.UtcNow.AddDays(nextStage.EstimatedDurationDays ?? 3),
                    AssignmentPriority.Medium,
                    completedAssignment.Id
                );
                try { autoInk.StartWork(); } catch { }
                db.Assignments.Add(autoInk);

                db.Notifications.Add(Domain.Entities.Workflow.Notification.Create(
                    completedAssignment.AssigneeUserId,
                    NotificationType.AssignmentAssigned,
                    "Bàn giao tự động Pencil ➔ Ink",
                    $"Công đoạn nét chì (Pencil) của Chương {chapter.ChapterNumber} đã hoàn tất. Hệ thống đã tự động gán mở việc Ink cho bạn.",
                    autoInk.Id,
                    chapter.Id));
            }
        }
        // 2. Chuyển giao sang Lettering (Ink ➔ Lettering):
        // Ngay khi bản vẽ Ink hoàn thành, hệ thống tự động mở task cho Letterer ráp bóng thoại và hiệu ứng âm thanh (SFX) dựa trên kịch bản đã khóa.
        else if (currentSlug.Contains("ink") || nextSlug.Contains("letter") || nextStage.AllowedRole == WorkspaceRole.Letterer)
        {
            var lettererStage = nextSlug.Contains("letter") ? nextStage : await db.PipelineStages
                .Where(s => s.PipelineDefinitionId == currentStage.PipelineDefinitionId && (s.Slug.Contains("letter") || s.AllowedRole == WorkspaceRole.Letterer) && s.IsEnabled)
                .OrderBy(s => s.StageOrder).FirstOrDefaultAsync(ct);

            if (lettererStage != null)
            {
                var letterAssignment = await db.Assignments
                    .FirstOrDefaultAsync(a => a.ChapterId == chapter.Id && a.PipelineStageId == lettererStage.Id, ct);

                if (letterAssignment is not null)
                {
                    db.Notifications.Add(Domain.Entities.Workflow.Notification.Create(
                        letterAssignment.AssigneeUserId,
                        NotificationType.AssignmentAssigned,
                        "Chuyển giao sang Lettering (Ink ➔ Lettering)",
                        $"Bản vẽ Ink của Chương {chapter.ChapterNumber} đã hoàn thành. Nhiệm vụ ráp bóng thoại và hiệu ứng âm thanh (SFX) đã sẵn sàng dựa trên kịch bản đã khóa.",
                        letterAssignment.Id,
                        chapter.Id));
                }
                else
                {
                    var series = await db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == chapter.SeriesId, ct);
                    Guid? lettererUserId = null;
                    if (series != null)
                    {
                        lettererUserId = await db.SeriesMembers.AsNoTracking()
                            .Where(sm => sm.SeriesId == series.Id && sm.Role == WorkspaceRole.Letterer)
                            .Select(sm => (Guid?)sm.UserId).FirstOrDefaultAsync(ct);

                        if (!lettererUserId.HasValue)
                        {
                            lettererUserId = await db.WorkspaceMembers.AsNoTracking()
                                .Where(wm => wm.WorkspaceId == series.WorkspaceId && wm.Role == WorkspaceRole.Letterer && wm.Status == WorkspaceMemberStatus.Active)
                                .Select(wm => (Guid?)wm.UserId).FirstOrDefaultAsync(ct);
                        }
                    }

                    if (lettererUserId.HasValue)
                    {
                        var autoLetter = Assignment.Create(
                            chapter.SeriesId,
                            chapter.Id,
                            WorkflowEntityType.Chapter,
                            chapter.Id,
                            lettererStage.Id,
                            lettererUserId.Value,
                            WorkspaceRole.Letterer,
                            $"Ráp bóng thoại và SFX Chương {chapter.ChapterNumber}",
                            "Chuyển giao tự động từ hoàn tất bản vẽ Ink (kịch bản đã khóa)",
                            null,
                            DateTime.UtcNow.AddDays(lettererStage.EstimatedDurationDays ?? 2),
                            AssignmentPriority.High,
                            completedAssignment.Id
                        );
                        db.Assignments.Add(autoLetter);

                        db.Notifications.Add(Domain.Entities.Workflow.Notification.Create(
                            lettererUserId.Value,
                            NotificationType.AssignmentAssigned,
                            "Mở task Lettering (Ink ➔ Lettering)",
                            $"Bản vẽ Ink của Chương {chapter.ChapterNumber} đã hoàn thành. Hệ thống đã mở nhiệm vụ ráp bóng thoại và SFX dựa trên kịch bản đã khóa.",
                            autoLetter.Id,
                            chapter.Id));
                    }
                }
            }
        }
        // 3. Gate 2 (Quality & Composite Gate: Lettering ➔ Editor Review):
        // Editor kiểm tra trang hoàn thiện tổng thể (bao gồm nét vẽ, vị trí bóng thoại, độ dễ đọc, ngắt dòng tiếng Việt).
        // Bắt lỗi trực quan trong bối cảnh hoàn chỉnh thay vì xem xét các thành phần rời rạc.
        else if (currentSlug.Contains("letter") || nextSlug.Contains("review") || nextStage.IsApprovalGate || nextStage.AllowedRole == WorkspaceRole.Editor)
        {
            var reviewStage = (nextSlug.Contains("review") || nextStage.IsApprovalGate || nextStage.AllowedRole == WorkspaceRole.Editor)
                ? nextStage
                : await db.PipelineStages
                    .Where(s => s.PipelineDefinitionId == currentStage.PipelineDefinitionId && (s.Slug.Contains("review") || s.IsApprovalGate || s.AllowedRole == WorkspaceRole.Editor) && s.IsEnabled)
                    .OrderBy(s => s.StageOrder).FirstOrDefaultAsync(ct);

            if (reviewStage != null)
            {
                var reviewAssignment = await db.Assignments
                    .FirstOrDefaultAsync(a => a.ChapterId == chapter.Id && a.PipelineStageId == reviewStage.Id, ct);

                if (reviewAssignment is not null)
                {
                    db.Notifications.Add(Domain.Entities.Workflow.Notification.Create(
                        reviewAssignment.AssigneeUserId,
                        NotificationType.AssignmentSubmitted,
                        "Gate 2 (Quality & Composite Gate): Sẵn sàng thẩm định",
                        $"Bản thảo Chương {chapter.ChapterNumber} đã hoàn tất ráp bóng thoại & SFX. Editor có thể tiến hành kiểm tra trang hoàn thiện tổng thể (nét vẽ, vị trí bóng thoại, độ dễ đọc, ngắt dòng tiếng Việt).",
                        reviewAssignment.Id,
                        chapter.Id));
                }
                else
                {
                    var series = await db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == chapter.SeriesId, ct);
                    Guid? editorUserId = null;
                    if (series != null)
                    {
                        editorUserId = await db.SeriesMembers.AsNoTracking()
                            .Where(sm => sm.SeriesId == series.Id && sm.Role == WorkspaceRole.Editor)
                            .Select(sm => (Guid?)sm.UserId).FirstOrDefaultAsync(ct);

                        if (!editorUserId.HasValue)
                        {
                            editorUserId = await db.WorkspaceMembers.AsNoTracking()
                                .Where(wm => wm.WorkspaceId == series.WorkspaceId && wm.Role == WorkspaceRole.Editor && wm.Status == WorkspaceMemberStatus.Active)
                                .Select(wm => (Guid?)wm.UserId).FirstOrDefaultAsync(ct);
                        }
                    }

                    if (editorUserId.HasValue)
                    {
                        var autoReview = Assignment.Create(
                            chapter.SeriesId,
                            chapter.Id,
                            WorkflowEntityType.Chapter,
                            chapter.Id,
                            reviewStage.Id,
                            editorUserId.Value,
                            WorkspaceRole.Editor,
                            $"Gate 2: Kiểm định tổng thể Chương {chapter.ChapterNumber}",
                            "Thẩm định chất lượng trang hoàn thiện tổng thể (nét vẽ, vị trí bóng thoại, độ dễ đọc, ngắt dòng tiếng Việt)",
                            null,
                            DateTime.UtcNow.AddDays(reviewStage.EstimatedDurationDays ?? 2),
                            AssignmentPriority.High,
                            completedAssignment.Id
                        );
                        db.Assignments.Add(autoReview);

                        db.Notifications.Add(Domain.Entities.Workflow.Notification.Create(
                            editorUserId.Value,
                            NotificationType.AssignmentSubmitted,
                            "Gate 2 (Quality & Composite Gate): Sẵn sàng thẩm định",
                            $"Bản thảo Chương {chapter.ChapterNumber} đã hoàn tất ráp bóng thoại & SFX. Hệ thống đã mở lượt kiểm định Gate 2 cho Editor kiểm tra trang hoàn thiện tổng thể.",
                            autoReview.Id,
                            chapter.Id));
                    }
                }
            }
        }

        // 3. Thông báo cho các việc phụ thuộc (DAG dependents)
        var idText = completedAssignment.Id.ToString();
        var waiting = await db.Assignments
            .Where(a => a.ChapterId == chapter.Id && a.CancelReason == null && a.Status == AssignmentStatus.Assigned
                        && (a.DependsOnAssignmentId == completedAssignment.Id || a.DependsOnAssignmentIdsJson.Contains(idText)))
            .ToListAsync(ct);

        if (waiting.Count > 0)
        {
            var depIds = waiting.SelectMany(a => a.GetDependsOnAssignmentIds()).Distinct().Where(id => id != completedAssignment.Id).ToList();
            var approvedIds = (await db.Assignments.AsNoTracking()
                    .Where(a => depIds.Contains(a.Id) && a.Status == AssignmentStatus.Approved)
                    .Select(a => a.Id).ToListAsync(ct))
                .Append(completedAssignment.Id).ToHashSet();

            foreach (var mine in waiting.Where(a => a.GetDependsOnAssignmentIds().All(approvedIds.Contains)).GroupBy(a => a.AssigneeUserId))
            {
                db.Notifications.Add(AssignmentNotifications.ReadyToStart(mine.OrderBy(a => a.CreatedAt).ToList()));
            }
        }
    }
}
