using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Queries.GetTaskGridReviewQueue;

/// <summary>Hàng đợi duyệt của lưới V3 trong Workspace (Clean Cutover: thay review-queue V2). Nộp sớm nhất trước.</summary>
public sealed record GetTaskGridReviewQueueQuery(Guid WorkspaceId, Guid? SeriesId = null)
    : IRequest<Result<IReadOnlyList<TaskGridReviewItemDto>>>;

public sealed class GetTaskGridReviewQueueQueryHandler
    : IRequestHandler<GetTaskGridReviewQueueQuery, Result<IReadOnlyList<TaskGridReviewItemDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetTaskGridReviewQueueQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<TaskGridReviewItemDto>>> Handle(GetTaskGridReviewQueueQuery q, CancellationToken ct)
    {
        var rows = await (
            from t in _db.StageTasks.AsNoTracking()
            join s in _db.Series.AsNoTracking() on t.SeriesId equals s.Id
            join c in _db.Chapters.AsNoTracking() on t.ChapterId equals c.Id
            join st in _db.PipelineStages.AsNoTracking() on t.PipelineStageId equals st.Id
            where s.WorkspaceId == q.WorkspaceId && (q.SeriesId == null || s.Id == q.SeriesId)
                && (t.Status == StageTaskStatus.Submitted || t.Status == StageTaskStatus.UnderReview)
            select new { Task = t, SeriesTitle = s.Title, c.ChapterNumber, ChapterTitle = c.Title, StageName = st.Name })
            .ToListAsync(ct);
        if (rows.Count == 0) return Result<IReadOnlyList<TaskGridReviewItemDto>>.Success([]);

        var lotIds = rows.Select(r => r.Task.LotId).OfType<Guid>().Distinct().ToList();
        var assignees = await _db.StageTaskLots.AsNoTracking().Where(l => lotIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.AssigneeUserId, ct);
        var pageIds = rows.Select(r => r.Task.PageId).OfType<Guid>().Distinct().ToList();
        var pageNumbers = await _db.Pages.AsNoTracking().Where(p => pageIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PageNumber, ct);
        var submissionIds = rows.Select(r => r.Task.CurrentSubmissionId).OfType<Guid>().ToList();
        var submissions = await _db.StageTaskSubmissions.AsNoTracking().Where(s => submissionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var result = rows.Select(r =>
        {
            var t = r.Task;
            var sub = t.CurrentSubmissionId is { } sid ? submissions.GetValueOrDefault(sid) : null;
            return new TaskGridReviewItemDto(
                t.Id, t.SeriesId, r.SeriesTitle, t.ChapterId, r.ChapterNumber, r.ChapterTitle, t.PipelineStageId, r.StageName,
                t.PageId, t.PageId is { } pid && pageNumbers.TryGetValue(pid, out var no) ? no : null, t.Status,
                t.LotId is { } lid && assignees.TryGetValue(lid, out var a) ? a : null, t.ReviewerUserId,
                sub?.AttemptNumber, sub?.SubmittedAt);
        })
            .OrderBy(i => i.SubmittedAt ?? DateTime.MaxValue).ThenBy(i => i.ChapterNumber).ThenBy(i => i.PageNumber ?? 0)
            .ToList();
        return Result<IReadOnlyList<TaskGridReviewItemDto>>.Success(result);
    }
}
