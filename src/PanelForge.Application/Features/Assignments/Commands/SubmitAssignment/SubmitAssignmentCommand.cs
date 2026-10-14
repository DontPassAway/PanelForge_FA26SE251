using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.SubmitAssignment;

/// <summary>
/// CF2 bước 7/11: người được giao nộp việc sang CF3. Điều kiện nộp theo artifact của stage
/// (script có dòng, artwork hash khớp...) sẽ thêm cùng StageProgress.
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

        try { assignment.SubmitForReview(); }
        catch (InvalidOperationException ex) { return Fail(ex.Message, ResultErrorCodes.Conflict); }

        if (await StageProgressRules.ApplyAsync(_db, stage, panels, cmd.UserId, submit: true, ct) is { } progressError)
            return Fail(progressError, ResultErrorCodes.Conflict);

        // CF3 bước 1: báo người duyệt (Editor; chưa có Editor thì Producer), cùng lần Save với việc nộp
        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
        foreach (var reviewerId in await AssignmentRules.ReviewerUserIdsAsync(_db, workspaceId, assignment.AssigneeUserId, ct))
            _db.Notifications.Add(AssignmentNotifications.Submitted(assignment, reviewerId));

        // Gắn lần nộp với đúng bản được nộp (CF3: mọi phê duyệt gắn với một revision cụ thể)
        // Chụp cả bố cục Panel của các trang đó: lịch sử lần nộp phải xem lại được dù sau này Panel bị xoá / đổi toạ độ (BR-01)
        var pageIds = panels.Select(p => p.PageId).Distinct().ToList();
        var pagePanels = pageIds.Count == 0
            ? []
            : await _db.Panels.AsNoTracking().Where(p => pageIds.Contains(p.PageId)).ToListAsync(ct);
        var artworkIds = await ChangeRequestRules.LatestArtworkIdsAsync(
            _db, assignment.PipelineStageId, pageIds, pagePanels.Select(p => p.Id), ct);
        await ChangeRequestRules.RecordSubmissionAsync(
            _db, assignment.Id, chapter.Id, cmd.UserId, ct, artworkIds, chapter.VersionVector,
            SubmissionPanelDto.Serialize(pagePanels));

        assignment.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<AssignmentDto>.Success(AssignmentDto.From(assignment));
    }
}
