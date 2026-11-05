using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.SubmitAssignment;

/// <summary>
/// CF2 bước 7/11: người được giao nộp việc sang CF3.
/// Nếu stage không yêu cầu Editor duyệt (như Pencil ➔ Ink, Ink ➔ Lettering): tự động duyệt và chuyển giao công đoạn tiếp theo.
/// </summary>
public sealed record SubmitAssignmentCommand(Guid ChapterId, Guid AssignmentId, Guid UserId)
    : IRequest<Result<AssignmentDto>>;

public sealed class SubmitAssignmentCommandHandler : IRequestHandler<SubmitAssignmentCommand, Result<AssignmentDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;

    public SubmitAssignmentCommandHandler(IPanelForgeDbContext db, IPageRepository pages)
    {
        _db = db;
        _pages = pages;
    }

    public async Task<Result<AssignmentDto>> Handle(SubmitAssignmentCommand cmd, CancellationToken ct)
    {
        static Result<AssignmentDto> Fail(string msg, string code) => Result<AssignmentDto>.Failure(msg, code);

        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId && a.ChapterId == cmd.ChapterId, ct);
        if (assignment is null)
            return Fail($"Assignment {cmd.AssignmentId} không tồn tại.", ResultErrorCodes.NotFound);

        if (assignment.AssigneeUserId != cmd.UserId)
            return Fail("Chỉ người được giao mới nộp được việc này.", ResultErrorCodes.Forbidden);

        var chapter = await _db.Chapters.AsNoTracking().FirstAsync(c => c.Id == cmd.ChapterId, ct);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Fail(locked, ResultErrorCodes.Conflict);

        // Phải qua Start (cổng BR-19) rồi mới nộp được
        if (assignment.Status != AssignmentStatus.InProgress)
            return Fail($"Phải bắt đầu việc trước khi nộp (hiện: {assignment.Status}).", ResultErrorCodes.Conflict);

        // CF3 bước 9: nộp lại chỉ khi mọi yêu cầu sửa đã được xử lý (không còn Open)
        var open = await ChangeRequestRules.UnclosedCountAsync(_db, assignment.Id, includeResolved: false, ct);
        if (open > 0)
            return Fail($"Còn {open} yêu cầu sửa chưa xử lý, hãy sửa và đánh dấu Resolved trước khi nộp lại.", ResultErrorCodes.Conflict);

        var panels = await StageProgressRules.ResolvePanelsAsync(_db, assignment, ct);
        if (await StageProgressRules.SubmissionBlockReasonAsync(_db, assignment, panels, ct) is { } blocked)
            return Fail(blocked, ResultErrorCodes.Conflict);
        if (await StageProgressRules.LetteringBlockReasonAsync(_db, _pages, assignment, panels, ct) is { } letteringBlocked)
            return Fail(letteringBlocked, ResultErrorCodes.Conflict);

        var stage = await _db.PipelineStages.AsNoTracking().FirstAsync(s => s.Id == assignment.PipelineStageId, ct);
        // Trang có thể bị trả về làm lại sau khi việc cấp Chapter đã bắt đầu (4.7): kiểm lại lúc nộp
        if (await StageProgressRules.ChapterWorkBlockReasonAsync(_db, assignment, stage, ct) is { } chapterBlocked)
            return Fail(chapterBlocked, ResultErrorCodes.Conflict);
        // Đầu vào có thể đã đổi trong lúc làm (stage trước / việc tiền đề bị mở lại): kiểm lại, không chỉ lúc bắt đầu
        if (await StageProgressRules.InputBlockReasonAsync(_db, assignment, stage, panels, ct) is { } inputBlocked)
            return Fail(inputBlocked, ResultErrorCodes.Conflict);

        var isAutoApproveStage = !stage.IsApprovalGate;

        try
        {
            if (isAutoApproveStage)
            {
                if (assignment.Status != AssignmentStatus.Approved)
                {
                    assignment.MarkApproved();
                }
            }
            else
            {
                if (assignment.Status == AssignmentStatus.Assigned || 
                    assignment.Status == AssignmentStatus.InProgress || 
                    assignment.Status == AssignmentStatus.ReworkRequired)
                {
                    assignment.SubmitForReview();
                }
            }
        }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        if (await StageProgressRules.ApplyAsync(_db, stage, panels, cmd.UserId, submit: true, ct, autoApprove: isAutoApproveStage) is { } progressError)
            return Fail(progressError, ResultErrorCodes.Conflict);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);

        if (!isAutoApproveStage)
        {
            // Báo người duyệt (Editor/Producer) khi stage có ApprovalGate (Gate 2: Quality & Composite Gate)
            foreach (var reviewerId in await AssignmentRules.ReviewerUserIdsAsync(_db, workspaceId, assignment.AssigneeUserId, ct))
            {
                var stageSlug = stage.Slug?.ToLowerInvariant() ?? "";
                var isGate2 = stageSlug.Contains("letter") || stageSlug.Contains("review") || stage.IsApprovalGate;
                var notifTitle = isGate2
                    ? "Gate 2 (Quality & Composite Gate): Chờ thẩm định"
                    : "Có việc mới được nộp chờ duyệt";
                var notifMsg = isGate2
                    ? $"Bản thảo hoàn thiện Chương {chapter.ChapterNumber} (nét vẽ, bóng thoại & SFX) đã được nộp để Editor kiểm tra tổng thể độ dễ đọc, ngắt dòng tiếng Việt và tính nhất quán."
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

        // Gắn lần nộp với đúng bản được nộp (CF3: mọi phê duyệt gắn với một revision cụ thể)
        var pageIds = panels.Select(p => p.PageId).Distinct().ToList();
        var pagePanels = pageIds.Count == 0
            ? []
            : await _db.Panels.AsNoTracking().Where(p => pageIds.Contains(p.PageId)).ToListAsync(ct);
        var artworkIds = await ChangeRequestRules.LatestArtworkIdsAsync(
            _db, assignment.PipelineStageId, pageIds, pagePanels.Select(p => p.Id), ct);

        var canvasVersions = new Dictionary<Guid, long>();
        foreach (var pageId in pageIds)
            canvasVersions[pageId] = await _pages.GetStreamVersionAsync(pageId, ct);

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
            await HandleAutoHandoffAsync(_db, chapter, stage, assignment, ct);
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
                // Tự động gán mở việc Ink cho nghệ sĩ hiện tại
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
