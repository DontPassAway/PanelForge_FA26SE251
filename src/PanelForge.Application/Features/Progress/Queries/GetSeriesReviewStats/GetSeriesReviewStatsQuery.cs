using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Progress.Queries.GetSeriesReviewStats;

/// <param name="WaitingReview">Việc đã nộp, chưa ai mở duyệt (Submitted).</param>
/// <param name="InReview">Việc đang được duyệt (UnderReview).</param>
/// <param name="ResolvedChangeRequests">Yêu cầu sửa đã được sửa, chờ Editor xác minh.</param>
/// <param name="OpenFindings">Phát hiện nhất quán chưa phân loại (Open / NeedsClarification).</param>
/// <param name="Approved">Lần nộp được duyệt bởi người khác người nộp (không tính công đoạn tự duyệt).</param>
/// <param name="FirstPassApproved">Số lần duyệt ngay ở lần nộp đầu.</param>
public sealed record ChapterReviewStatsDto(
    Guid ChapterId, decimal ChapterNumber, string? Title, ChapterStatus Status, ScriptStatus ScriptStatus,
    int WaitingReview, int InReview,
    int OpenChangeRequests, int ResolvedChangeRequests, int VerifiedChangeRequests, int OverdueChangeRequests,
    int OpenFindings, int BlockingFindings,
    int Approved, int Rejected, int FirstPassApproved,
    int TasksTotal = 0, int TasksApproved = 0, int TasksInProgress = 0, int TasksRework = 0, int TasksOverdue = 0);

/// <summary>Trạng thái thiết kế Series Bible theo revision mới nhất của mỗi mục (bỏ mục stub).</summary>
public sealed record BibleReviewSummaryDto(int Entries, int Draft, int ConceptSubmitted, int ReworkRequired, int CanonLocked);

public sealed record ReviewDecisionPointDto(DateOnly Date, int Approved, int Rejected);

/// <param name="Decisions">Số quyết định duyệt / trả mỗi ngày (UTC) trong <see cref="GetSeriesReviewStatsQueryHandler.WindowDays"/> ngày gần nhất.</param>
/// <param name="AvgReviewHours">Thời gian trung bình từ lúc nộp tới lúc có quyết định, trong cùng khoảng; null khi chưa có quyết định.</param>
public sealed record SeriesReviewStatsDto(
    Guid SeriesId, DateTime GeneratedAt, IReadOnlyList<ChapterReviewStatsDto> Chapters,
    IReadOnlyList<ReviewDecisionPointDto> Decisions, double? AvgReviewHours,
    BibleReviewSummaryDto? Bible = null);

/// <summary>
/// Tổng quan Series cho Editor (CF3): kịch bản, việc sản xuất, hàng chờ duyệt, yêu cầu sửa, phát hiện nhất quán
/// và kết quả duyệt theo chương, cùng trạng thái thiết kế Series Bible.
/// Gộp việc V2 (Assignment/Submission) và ô lưới V3 (StageTask/StageTaskSubmission). Chỉ đọc.
/// </summary>
public sealed record GetSeriesReviewStatsQuery(Guid SeriesId) : IRequest<Result<SeriesReviewStatsDto>>;

public sealed class GetSeriesReviewStatsQueryHandler : IRequestHandler<GetSeriesReviewStatsQuery, Result<SeriesReviewStatsDto>>
{
    public const int WindowDays = 30;

    private readonly IPanelForgeDbContext _db;

    public GetSeriesReviewStatsQueryHandler(IPanelForgeDbContext db) => _db = db;

    private sealed record Decision(Guid ChapterId, SubmissionStatus Status, int Attempt, DateTime SubmittedAt, DateTime DecidedAt);

    public async Task<Result<SeriesReviewStatsDto>> Handle(GetSeriesReviewStatsQuery q, CancellationToken ct)
    {
        if (!await _db.Series.AsNoTracking().AnyAsync(s => s.Id == q.SeriesId, ct))
            return Result<SeriesReviewStatsDto>.Failure($"Series {q.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        var now = DateTime.UtcNow;
        var chapters = await _db.Chapters.AsNoTracking().Where(c => c.SeriesId == q.SeriesId)
            .OrderBy(c => c.ChapterNumber)
            .Select(c => new { c.Id, c.ChapterNumber, c.Title, c.Status, c.ScriptStatus })
            .ToListAsync(ct);
        var chapterIds = chapters.Select(c => c.Id).ToList();

        var taskStatuses = await _db.StageTasks.AsNoTracking()
            .Where(t => t.SeriesId == q.SeriesId
                        && (t.Status == StageTaskStatus.Submitted || t.Status == StageTaskStatus.UnderReview))
            .Select(t => new { t.ChapterId, InReview = t.Status == StageTaskStatus.UnderReview })
            .ToListAsync(ct);
        var assignmentStatuses = await _db.Assignments.AsNoTracking()
            .Where(a => a.SeriesId == q.SeriesId
                        && (a.Status == AssignmentStatus.Submitted || a.Status == AssignmentStatus.UnderReview))
            .Select(a => new { a.ChapterId, InReview = a.Status == AssignmentStatus.UnderReview })
            .ToListAsync(ct);
        var queue = taskStatuses.Concat(assignmentStatuses).ToList();

        // Mọi việc đã giao (ô lưới V3 qua lô + Assignment V2) để tính tiến độ sản xuất của chương
        var gridTasks = await (from t in _db.StageTasks.AsNoTracking()
                               join l in _db.StageTaskLots.AsNoTracking() on t.LotId equals (Guid?)l.Id
                               where t.SeriesId == q.SeriesId && t.Status != StageTaskStatus.Unassigned && t.Status != StageTaskStatus.Cancelled
                               select new { t.ChapterId, Status = t.Status.ToString(), l.DueDate })
                              .ToListAsync(ct);
        var legacyTasks = await _db.Assignments.AsNoTracking().Where(a => a.SeriesId == q.SeriesId)
            .Select(a => new { a.ChapterId, Status = a.Status.ToString(), a.DueDate })
            .ToListAsync(ct);
        var work = gridTasks.Concat(legacyTasks).ToList();

        var changeRequests = await _db.ChangeRequests.AsNoTracking()
            .Where(cr => chapterIds.Contains(cr.ChapterId))
            .Select(cr => new { cr.ChapterId, cr.Status, cr.DueDate })
            .ToListAsync(ct);

        var findings = await _db.ConsistencyFindings.AsNoTracking()
            .Where(f => chapterIds.Contains(f.ChapterId)
                        && (f.Status == ConsistencyFindingStatus.Open || f.Status == ConsistencyFindingStatus.NeedsClarification))
            .Select(f => new { f.ChapterId, f.Severity })
            .ToListAsync(ct);

        // Lần nộp tự duyệt (công đoạn không có cổng) có DecidedBy = người nộp: không phải quyết định của Editor
        var decisions = (await _db.Submissions.AsNoTracking()
                .Where(s => chapterIds.Contains(s.ChapterId) && s.DecidedAt != null && s.DecidedByUserId != s.SubmittedByUserId
                            && (s.Status == SubmissionStatus.Approved || s.Status == SubmissionStatus.Rejected))
                .Select(s => new Decision(s.ChapterId, s.Status, s.AttemptNumber, s.SubmittedAt, s.DecidedAt!.Value))
                .ToListAsync(ct))
            .Concat(await _db.StageTaskSubmissions.AsNoTracking()
                .Where(s => chapterIds.Contains(s.ChapterId) && s.DecidedAt != null && s.DecidedByUserId != s.SubmittedByUserId
                            && (s.Status == SubmissionStatus.Approved || s.Status == SubmissionStatus.Rejected))
                .Select(s => new Decision(s.ChapterId, s.Status, s.AttemptNumber, s.SubmittedAt, s.DecidedAt!.Value))
                .ToListAsync(ct))
            .ToList();

        var chapterDtos = chapters.Select(c =>
        {
            var crs = changeRequests.Where(cr => cr.ChapterId == c.Id).ToList();
            var fs = findings.Where(f => f.ChapterId == c.Id).ToList();
            var ds = decisions.Where(d => d.ChapterId == c.Id).ToList();
            var qs = queue.Where(x => x.ChapterId == c.Id).ToList();
            var ws = work.Where(w => w.ChapterId == c.Id).ToList();
            static bool Is(string status, string name) => status == name;
            return new ChapterReviewStatsDto(
                c.Id, c.ChapterNumber, c.Title, c.Status, c.ScriptStatus,
                WaitingReview: qs.Count(x => !x.InReview),
                InReview: qs.Count(x => x.InReview),
                OpenChangeRequests: crs.Count(cr => cr.Status == ChangeRequestStatus.Open),
                ResolvedChangeRequests: crs.Count(cr => cr.Status == ChangeRequestStatus.Resolved),
                VerifiedChangeRequests: crs.Count(cr => cr.Status == ChangeRequestStatus.Verified),
                OverdueChangeRequests: crs.Count(cr => cr.Status == ChangeRequestStatus.Open && cr.DueDate is { } due && due < now),
                OpenFindings: fs.Count,
                BlockingFindings: fs.Count(f => f.Severity == ConsistencyFindingSeverity.Blocking),
                Approved: ds.Count(d => d.Status == SubmissionStatus.Approved),
                Rejected: ds.Count(d => d.Status == SubmissionStatus.Rejected),
                FirstPassApproved: ds.Count(d => d.Status == SubmissionStatus.Approved && d.Attempt == 1),
                TasksTotal: ws.Count,
                TasksApproved: ws.Count(w => Is(w.Status, "Approved")),
                TasksInProgress: ws.Count(w => Is(w.Status, "Assigned") || Is(w.Status, "InProgress")),
                TasksRework: ws.Count(w => Is(w.Status, "ReworkRequired")),
                TasksOverdue: ws.Count(w => w.DueDate is { } due && due < now && !Is(w.Status, "Approved")));
        }).ToList();

        var today = DateOnly.FromDateTime(now);
        var from = today.AddDays(-(WindowDays - 1));
        var recent = decisions.Where(d => DateOnly.FromDateTime(d.DecidedAt) >= from).ToList();
        var points = Enumerable.Range(0, WindowDays).Select(i =>
        {
            var day = from.AddDays(i);
            var onDay = recent.Where(d => DateOnly.FromDateTime(d.DecidedAt) == day).ToList();
            return new ReviewDecisionPointDto(day,
                onDay.Count(d => d.Status == SubmissionStatus.Approved),
                onDay.Count(d => d.Status == SubmissionStatus.Rejected));
        }).ToList();
        double? avgHours = recent.Count == 0
            ? null
            : Math.Round(recent.Average(d => (d.DecidedAt - d.SubmittedAt).TotalHours), 1);

        // Revision mới nhất của mỗi mục Bible quyết định trạng thái thiết kế hiện tại
        var bibleStatuses = await _db.BibleEntries.AsNoTracking()
            .Where(e => !e.IsStub && _db.SeriesBibles.Any(b => b.Id == e.SeriesBibleId && b.SeriesId == q.SeriesId))
            .Select(e => e.Revisions.OrderByDescending(r => r.VersionNumber).Select(r => (BibleRevisionStatus?)r.ReviewStatus).FirstOrDefault())
            .ToListAsync(ct);
        var bible = new BibleReviewSummaryDto(
            bibleStatuses.Count,
            bibleStatuses.Count(s => s is null or BibleRevisionStatus.Draft),
            bibleStatuses.Count(s => s == BibleRevisionStatus.ConceptSubmitted),
            bibleStatuses.Count(s => s == BibleRevisionStatus.ReworkRequired),
            bibleStatuses.Count(s => s == BibleRevisionStatus.CanonLocked));

        return Result<SeriesReviewStatsDto>.Success(new SeriesReviewStatsDto(q.SeriesId, now, chapterDtos, points, avgHours, bible));
    }
}
