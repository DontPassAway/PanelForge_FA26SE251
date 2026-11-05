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
/// Nộp bản thảo toàn bộ chương trực tiếp từ Bàn vẽ Artist mà không yêu cầu phụ thuộc vào cấu hình Assign trước đó.
/// Tự động gắn/tạo tiến trình và gửi thông báo thẩm định sang Editor/Producer.
/// </summary>
public sealed record SubmitChapterManuscriptCommand(Guid ChapterId, Guid UserId)
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

        // 1. Tìm assignment cấp Chapter hoặc việc của user
        var existingAssignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.ChapterId == cmd.ChapterId &&
                (a.AssigneeUserId == cmd.UserId || a.EntityType == WorkflowEntityType.Chapter), ct);

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
            // Tự động tìm stage vẽ đầu tiên trong pipeline
            var pipeline = await _db.PipelineDefinitions.AsNoTracking().FirstOrDefaultAsync(p => p.SeriesId == chapter.SeriesId, ct);
            var stage = pipeline != null
                ? await _db.PipelineStages.AsNoTracking()
                    .Where(s => s.PipelineDefinitionId == pipeline.Id && s.IsEnabled != false)
                    .OrderBy(s => s.StageOrder)
                    .FirstOrDefaultAsync(ct)
                : null;

            if (stage == null)
            {
                stage = await _db.PipelineStages.AsNoTracking()
                    .Where(s => s.IsEnabled != false)
                    .OrderBy(s => s.StageOrder)
                    .FirstOrDefaultAsync(ct);
            }

            var stageId = stage?.Id ?? Guid.NewGuid();

            assignment = Assignment.Create(
                chapter.SeriesId,
                chapter.Id,
                WorkflowEntityType.Chapter,
                chapter.Id,
                stageId,
                cmd.UserId,
                WorkspaceRole.Artist,
                $"Bản thảo Chương {chapter.ChapterNumber}",
                "Bản thảo nộp trực tiếp từ Bàn vẽ Artist",
                null,
                null,
                AssignmentPriority.Medium
            );
            try { assignment.StartWork(); } catch { }
            _db.Assignments.Add(assignment);
        }

        // Đánh dấu Submit
        try
        {
            if (assignment.Status != AssignmentStatus.Submitted && assignment.Status != AssignmentStatus.UnderReview)
            {
                assignment.SubmitForReview();
            }
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message, ResultErrorCodes.Conflict);
        }

        // Báo người duyệt (Editor / Producer)
        foreach (var reviewerId in await AssignmentRules.ReviewerUserIdsAsync(_db, series.WorkspaceId, cmd.UserId, ct))
        {
            _db.Notifications.Add(AssignmentNotifications.Submitted(assignment, reviewerId));
        }

        // Ghi lại submission
        var pages = await _db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id).ToListAsync(ct);
        var pageIds = pages.Select(p => p.Id).ToList();
        var pagePanels = await _db.Panels.AsNoTracking().Where(p => pageIds.Contains(p.PageId)).ToListAsync(ct);
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

        assignment.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<AssignmentDto>.Success(AssignmentDto.From(assignment));
    }
}
