using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Queries.GetAutomationActivity;

/// <summary>Một sự kiện Automation: <c>Kind</c> = AutoAssigned | Skipped | Escalated.</summary>
public sealed record AutomationActivityItemDto(
    string Kind,
    DateTime OccurredAt,
    Guid SeriesId,
    string SeriesTitle,
    Guid ChapterId,
    decimal ChapterNumber,
    string? StageName,
    IReadOnlyList<int> PageNumbers,
    Guid? AssigneeUserId,
    string? AssigneeName,
    string? Reason);

public sealed record AutomationActivitySummaryDto(int AutoAssigned, int Skipped, int Escalated);

public sealed record AutomationActivityDto(
    AutomationActivitySummaryDto Last7Days,
    IReadOnlyList<AutomationActivityItemDto> Items);

/// <summary>
/// Assign Automation: nhật ký gần đây của Studio để Producer/Editor biết R1/R3 có đang chạy.
/// Chỉ đọc dữ liệu có sẵn: lô có cờ tự giao, thông báo R1 bỏ qua và R3 báo trễ (gửi cho nhiều Producer thì gộp một dòng).
/// </summary>
public sealed record GetAutomationActivityQuery(Guid UserId, Guid WorkspaceId, int Take = 20)
    : IRequest<Result<AutomationActivityDto>>;

public sealed class GetAutomationActivityQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    : IRequestHandler<GetAutomationActivityQuery, Result<AutomationActivityDto>>
{
    public const string AutoAssigned = "AutoAssigned", Skipped = "Skipped", Escalated = "Escalated";

    public async Task<Result<AutomationActivityDto>> Handle(GetAutomationActivityQuery q, CancellationToken ct)
    {
        if (!await auth.HasWorkspaceRoleAsync(q.UserId, q.WorkspaceId, [WorkspaceRole.Producer, WorkspaceRole.Editor], ct))
            return Result<AutomationActivityDto>.Failure("Chỉ Producer hoặc Editor xem được nhật ký Automation.", ResultErrorCodes.Forbidden);

        var take = Math.Clamp(q.Take, 1, 100);
        var since = DateTime.UtcNow.AddDays(-7);
        var chapters = from c in db.Chapters.AsNoTracking()
                       join s in db.Series.AsNoTracking() on c.SeriesId equals s.Id
                       where s.WorkspaceId == q.WorkspaceId
                       select new { c.Id, c.ChapterNumber, SeriesId = s.Id, SeriesTitle = s.Title };

        var lots = await (from l in db.StageTaskLots.AsNoTracking()
                          where l.IsAutoAssigned
                          join c in chapters on l.ChapterId equals c.Id
                          join st in db.PipelineStages.AsNoTracking() on l.PipelineStageId equals st.Id
                          join u in db.Users.AsNoTracking() on l.AssigneeUserId equals u.Id
                          orderby l.CreatedAt descending
                          select new { l.Id, l.CreatedAt, c.SeriesId, c.SeriesTitle, ChapterId = c.Id, c.ChapterNumber, Stage = st.Name,
                                       l.AssigneeUserId, u.FullName, l.AutoAssignReason })
            .Take(take).ToListAsync(ct);
        var lotIds = lots.Select(l => l.Id).ToList();
        var pagesByLot = (await (from t in db.StageTasks.AsNoTracking()
                                 where t.LotId != null && lotIds.Contains(t.LotId.Value)
                                 join p in db.Pages.AsNoTracking() on t.PageId equals p.Id
                                 select new { LotId = t.LotId!.Value, p.PageNumber }).ToListAsync(ct))
            .ToLookup(x => x.LotId, x => x.PageNumber);

        // Thông báo gửi cho từng Producer/Owner: gộp theo nội dung để mỗi sự kiện chỉ một dòng
        var notes = (await (from n in db.Notifications.AsNoTracking()
                            where n.ChapterId != null &&
                                  (n.Title.Contains(TaskGridNotifications.SkippedTitleMarker) ||
                                   n.Title.StartsWith(TaskGridNotifications.EscalatedTitlePrefix))
                            join c in chapters on n.ChapterId equals c.Id
                            orderby n.CreatedAt descending
                            select new { n.Title, n.Message, n.CreatedAt, c.SeriesId, c.SeriesTitle, ChapterId = c.Id, c.ChapterNumber })
                .Take(take * 5).ToListAsync(ct))
            .GroupBy(n => (n.ChapterId, n.Title, n.Message)).Select(g => g.First()).Take(take).ToList();

        var items = lots.Select(l => new AutomationActivityItemDto(
                AutoAssigned, l.CreatedAt, l.SeriesId, l.SeriesTitle, l.ChapterId, l.ChapterNumber, l.Stage,
                pagesByLot[l.Id].Distinct().Order().ToList(), l.AssigneeUserId, l.FullName, l.AutoAssignReason))
            .Concat(notes.Select(n => new AutomationActivityItemDto(
                n.Title.StartsWith(TaskGridNotifications.EscalatedTitlePrefix) ? Escalated : Skipped,
                n.CreatedAt, n.SeriesId, n.SeriesTitle, n.ChapterId, n.ChapterNumber, null, [], null, null, n.Message)))
            .OrderByDescending(i => i.OccurredAt).Take(take).ToList();

        var lotCount = await db.StageTaskLots.AsNoTracking()
            .CountAsync(l => l.IsAutoAssigned && l.CreatedAt >= since && chapters.Any(c => c.Id == l.ChapterId), ct);
        var recentNotes = await (from n in db.Notifications.AsNoTracking()
                                 where n.CreatedAt >= since && n.ChapterId != null &&
                                       (n.Title.Contains(TaskGridNotifications.SkippedTitleMarker) ||
                                        n.Title.StartsWith(TaskGridNotifications.EscalatedTitlePrefix))
                                 join c in chapters on n.ChapterId equals c.Id
                                 select new { n.ChapterId, n.Title, n.Message }).Distinct().ToListAsync(ct);
        var escalated = recentNotes.Count(n => n.Title.StartsWith(TaskGridNotifications.EscalatedTitlePrefix));

        return Result<AutomationActivityDto>.Success(new AutomationActivityDto(
            new AutomationActivitySummaryDto(lotCount, recentNotes.Count - escalated, escalated), items));
    }
}
