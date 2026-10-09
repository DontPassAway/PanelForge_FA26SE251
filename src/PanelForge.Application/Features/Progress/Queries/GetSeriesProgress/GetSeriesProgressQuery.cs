using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Progress.Queries.GetSeriesProgress;

/// <summary>Số Panel ở từng trạng thái của một Stage. Panel chưa có StageProgress ở stage đó tính vào NotStarted.</summary>
public sealed record StageBreakdownDto(
    Guid StageId, string Name, int StageOrder,
    int NotStarted, int InProgress, int Submitted, int InReview, int ChangesRequested, int Approved);

public sealed record ChapterProgressDto(
    Guid ChapterId, decimal ChapterNumber, string? Title, ChapterStatus Status, DateOnly? TargetReleaseDate,
    int PageCount, int PanelCount, double PercentComplete, IReadOnlyList<StageBreakdownDto> Stages,
    IReadOnlyDictionary<string, int> AssignmentsByStatus, int OverdueAssignments);

public sealed record WorkloadDto(
    Guid UserId, string FullName, WorkspaceRole Role, int Open, int Overdue, int WaitingReview, int Done);

public sealed record OverdueAssignmentDto(
    Guid AssignmentId, Guid ChapterId, Guid AssigneeUserId, string AssigneeName, AssignmentStatus Status, DateTime DueDate, string Brief);

/// <param name="Remaining">Số việc chưa Approved vào cuối ngày <paramref name="Date"/> (UTC).</param>
public sealed record BurndownPointDto(DateOnly Date, int Remaining);

public sealed record SeriesProgressDto(
    Guid SeriesId, DateTime GeneratedAt, IReadOnlyList<ChapterProgressDto> Chapters, IReadOnlyList<WorkloadDto> Workload,
    IReadOnlyList<OverdueAssignmentDto> Overdue, IReadOnlyList<BurndownPointDto> Burndown);

/// <summary>
/// UC-16: tổng hợp tiến độ của Series cho Producer: theo stage và Panel, khối lượng việc từng người, việc quá hạn, burndown.
/// Tiến độ lấy từ StageProgress (nguồn sự thật duy nhất); <c>PercentComplete</c> = Panel Approved ở stage làm việc cuối
/// (bỏ qua stage terminal) / số Panel.
/// </summary>
public sealed record GetSeriesProgressQuery(Guid SeriesId) : IRequest<Result<SeriesProgressDto>>;

public sealed class GetSeriesProgressQueryHandler : IRequestHandler<GetSeriesProgressQuery, Result<SeriesProgressDto>>
{
    private const int MaxBurndownDays = 90;

    private readonly IPanelForgeDbContext _db;

    public GetSeriesProgressQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<SeriesProgressDto>> Handle(GetSeriesProgressQuery q, CancellationToken ct)
    {
        if (!await _db.Series.AsNoTracking().AnyAsync(s => s.Id == q.SeriesId, ct))
            return Result<SeriesProgressDto>.Failure($"Series {q.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        var now = DateTime.UtcNow;

        var stages = await _db.PipelineStages.AsNoTracking()
            .Where(s => _db.PipelineDefinitions.Any(d => d.Id == s.PipelineDefinitionId && d.SeriesId == q.SeriesId))
            .OrderBy(s => s.StageOrder).Select(s => new { s.Id, s.Name, s.StageOrder, s.IsTerminal }).ToListAsync(ct);
        // Stage terminal ("Approved") chỉ là nhãn kết thúc, không ai làm việc ở đó: hoàn thành = duyệt xong stage làm việc cuối
        var finalStageId = (stages.LastOrDefault(s => !s.IsTerminal) ?? stages.LastOrDefault())?.Id;

        var chapters = await _db.Chapters.AsNoTracking().Where(c => c.SeriesId == q.SeriesId)
            .OrderBy(c => c.ChapterNumber).ToListAsync(ct);
        var chapterIds = chapters.Select(c => c.Id).ToList();

        var pages = await _db.Pages.AsNoTracking().Where(p => chapterIds.Contains(p.ChapterId))
            .Select(p => new { p.Id, p.ChapterId }).ToListAsync(ct);
        var pageIds = pages.Select(p => p.Id).ToList();
        var panels = await _db.Panels.AsNoTracking().Where(p => pageIds.Contains(p.PageId))
            .Select(p => new { p.Id, p.PageId }).ToListAsync(ct);
        var panelIds = panels.Select(p => p.Id).ToList();
        var progress = await _db.StageProgresses.AsNoTracking().Where(s => panelIds.Contains(s.PanelId)).ToListAsync(ct);
        var assignments = await _db.Assignments.AsNoTracking().Where(a => a.SeriesId == q.SeriesId).ToListAsync(ct);

        var pageChapter = pages.ToDictionary(p => p.Id, p => p.ChapterId);
        var panelChapter = panels.ToDictionary(p => p.Id, p => pageChapter[p.PageId]);

        bool IsOverdue(Assignment a) => a.DueDate is { } due && due < now && a.Status != AssignmentStatus.Approved;

        var chapterDtos = chapters.Select(c =>
        {
            var chapterPanels = panels.Where(p => panelChapter[p.Id] == c.Id).Select(p => p.Id).ToHashSet();
            var rows = progress.Where(r => chapterPanels.Contains(r.PanelId)).ToList();
            var breakdown = stages.Select(s =>
            {
                int Count(StageProgressStatus st) => rows.Count(r => r.PipelineStageId == s.Id && r.Status == st);
                var started = rows.Count(r => r.PipelineStageId == s.Id);
                return new StageBreakdownDto(s.Id, s.Name, s.StageOrder, chapterPanels.Count - started,
                    Count(StageProgressStatus.InProgress), Count(StageProgressStatus.Submitted), Count(StageProgressStatus.InReview),
                    Count(StageProgressStatus.ChangesRequested), Count(StageProgressStatus.Approved));
            }).ToList();

            var finalApproved = finalStageId is null ? 0
                : rows.Count(r => r.PipelineStageId == finalStageId && r.Status == StageProgressStatus.Approved);
            var percent = chapterPanels.Count == 0 ? 0 : Math.Round(100.0 * finalApproved / chapterPanels.Count, 1);
            var mine = assignments.Where(a => a.ChapterId == c.Id).ToList();

            return new ChapterProgressDto(
                c.Id, c.ChapterNumber, c.Title, c.Status, c.TargetReleaseDate,
                pages.Count(p => p.ChapterId == c.Id), chapterPanels.Count, percent, breakdown,
                mine.GroupBy(a => a.Status.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                mine.Count(IsOverdue));
        }).ToList();

        var assigneeIds = assignments.Select(a => a.AssigneeUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => assigneeIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var workload = assignments.GroupBy(a => new { a.AssigneeUserId, a.AssignedRole })
            .Select(g => new WorkloadDto(
                g.Key.AssigneeUserId, names.GetValueOrDefault(g.Key.AssigneeUserId, "?"), g.Key.AssignedRole,
                g.Count(a => a.Status != AssignmentStatus.Approved), g.Count(IsOverdue),
                g.Count(a => a.Status is AssignmentStatus.Submitted or AssignmentStatus.UnderReview),
                g.Count(a => a.Status == AssignmentStatus.Approved)))
            .OrderByDescending(w => w.Open).ThenBy(w => w.FullName).ToList();

        var overdue = assignments.Where(IsOverdue).OrderBy(a => a.DueDate)
            .Select(a => new OverdueAssignmentDto(a.Id, a.ChapterId, a.AssigneeUserId,
                names.GetValueOrDefault(a.AssigneeUserId, "?"), a.Status, a.DueDate!.Value, a.Brief)).ToList();

        // Burndown theo số việc: còn lại(ngày) = đã tạo đến ngày đó - đã Approved đến ngày đó (ngày UTC, tối đa 90 ngày gần nhất)
        var burndown = new List<BurndownPointDto>();
        if (assignments.Count > 0)
        {
            var today = DateOnly.FromDateTime(now);
            var first = DateOnly.FromDateTime(assignments.Min(a => a.CreatedAt));
            var from = first < today.AddDays(-(MaxBurndownDays - 1)) ? today.AddDays(-(MaxBurndownDays - 1)) : first;
            for (var d = from; d <= today; d = d.AddDays(1))
            {
                var endOfDay = d.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                burndown.Add(new BurndownPointDto(d,
                    assignments.Count(a => a.CreatedAt < endOfDay) - assignments.Count(a => a.CompletedAt != null && a.CompletedAt < endOfDay)));
            }
        }

        return Result<SeriesProgressDto>.Success(new SeriesProgressDto(q.SeriesId, now, chapterDtos, workload, overdue, burndown));
    }
}
