using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Notifications.Commands.SendAssignmentReminders;

/// <summary>
/// UC-14 / CF2 bước 2: nhắc người nhận trước hạn. Việc chưa xong (Assigned, InProgress, ReworkRequired, Blocked) của Chapter còn
/// InProduction có hạn trong <paramref name="Lead"/> tới thì nhận "sắp đến hạn"; đã quá hạn (trong 7 ngày gần nhất) thì nhận
/// "đã quá hạn" một lần. Việc đã nộp chờ duyệt, đã duyệt, hay Chapter đã khoá thì không nhắc. Trả về số thông báo mới.
/// Chống gửi trùng bằng cách đối chiếu thông báo cùng việc, loại, người nhận và nội dung (nội dung chứa hạn nên đổi hạn thì nhắc lại).
/// <paramref name="ProducerUserId"/> có giá trị (gọi tay từ API) thì chỉ quét các Workspace người đó làm Producer; null (job nền) quét tất cả.
/// </summary>
public sealed record SendAssignmentRemindersCommand(TimeSpan Lead, DateTime? NowUtc = null, Guid? ProducerUserId = null) : IRequest<int>;

public sealed class SendAssignmentRemindersCommandHandler : IRequestHandler<SendAssignmentRemindersCommand, int>
{
    private static readonly TimeSpan OverdueWindow = TimeSpan.FromDays(7);
    private static readonly AssignmentStatus[] OpenStatuses =
        [AssignmentStatus.Assigned, AssignmentStatus.InProgress, AssignmentStatus.ReworkRequired, AssignmentStatus.Blocked];

    private readonly IPanelForgeDbContext _db;

    public SendAssignmentRemindersCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<int> Handle(SendAssignmentRemindersCommand cmd, CancellationToken ct)
    {
        var now = cmd.NowUtc ?? DateTime.UtcNow;
        var upper = now + cmd.Lead;
        var lower = now - OverdueWindow;

        var pid = cmd.ProducerUserId;
        var chapterIds = _db.Chapters
            .Where(c => c.Status == ChapterStatus.InProduction &&
                        (pid == null || _db.Series.Any(s => s.Id == c.SeriesId &&
                            (_db.StudioWorkspaces.Any(w => w.Id == s.WorkspaceId && w.OwnerId == pid) ||
                             _db.WorkspaceMembers.Any(m => m.WorkspaceId == s.WorkspaceId && m.UserId == pid
                                 && m.Role == WorkspaceRole.Producer && m.Status == WorkspaceMemberStatus.Active)))))
            .Select(c => c.Id);

        var candidates = await _db.Assignments.AsNoTracking()
            .Where(a => a.DueDate != null && a.DueDate <= upper && a.DueDate >= lower && OpenStatuses.Contains(a.Status) &&
                        chapterIds.Contains(a.ChapterId))
            .ToListAsync(ct);

        var created = 0;
        foreach (var a in candidates)
        {
            var note = a.DueDate < now ? AssignmentNotifications.Overdue(a) : AssignmentNotifications.DueSoon(a);

            var alreadySent = await _db.Notifications.AnyAsync(n =>
                n.AssignmentId == a.Id && n.UserId == a.AssigneeUserId && n.Type == note.Type && n.Message == note.Message, ct);
            if (alreadySent) continue;

            _db.Notifications.Add(note);
            created++;
        }

        // ChangeRequest: nhắc người chịu trách nhiệm khi yêu cầu sửa sắp đến hạn hoặc đã quá hạn (UC-14, CF3)
        var crCandidates = await _db.ChangeRequests.AsNoTracking()
            .Where(cr => cr.DueDate != null && cr.DueDate <= upper && cr.DueDate >= lower && cr.Status == ChangeRequestStatus.Open &&
                         chapterIds.Contains(cr.ChapterId))
            .ToListAsync(ct);

        foreach (var cr in crCandidates)
        {
            var note = cr.DueDate < now ? AssignmentNotifications.ChangeRequestOverdue(cr) : AssignmentNotifications.ChangeRequestDueSoon(cr);

            var alreadySent = await _db.Notifications.AnyAsync(n =>
                n.AssignmentId == cr.WorkItemId && n.UserId == cr.ResponsibleUserId && n.Type == note.Type && n.Message == note.Message, ct);
            if (alreadySent) continue;

            _db.Notifications.Add(note);
            created++;
        }

        // Lưới V3 (R3): giờ nhắc theo Studio (chưa lưu cấu hình thì dùng cmd.Lead như trước). Workspace của từng chương đang sản xuất:
        var chapterWorkspace = await (from c in _db.Chapters.AsNoTracking()
                                      join s in _db.Series.AsNoTracking() on c.SeriesId equals s.Id
                                      where chapterIds.Contains(c.Id)
                                      select new { c.Id, c.ChapterNumber, s.WorkspaceId }).ToListAsync(ct);
        var workspaceIds = chapterWorkspace.Select(c => c.WorkspaceId).Distinct().ToList();
        var settings = await _db.WorkspaceAutomationSettings.AsNoTracking().Where(s => workspaceIds.Contains(s.WorkspaceId))
            .ToDictionaryAsync(s => s.WorkspaceId, ct);
        var workspaceOf = chapterWorkspace.ToDictionary(c => c.Id, c => c.WorkspaceId);
        TimeSpan LeadOf(Guid chapterId) =>
            settings.TryGetValue(workspaceOf.GetValueOrDefault(chapterId), out var s) ? TimeSpan.FromHours(s.LeadHours) : cmd.Lead;
        int? EscalationOf(Guid workspaceId) => settings.TryGetValue(workspaceId, out var s) ? s.EscalationHours : null;

        var producersCache = new Dictionary<Guid, List<Guid>>();
        async Task<List<Guid>> ProducersOf(Guid workspaceId)
        {
            if (producersCache.TryGetValue(workspaceId, out var cached)) return cached;
            var ownerId = await _db.StudioWorkspaces.AsNoTracking().Where(w => w.Id == workspaceId).Select(w => w.OwnerId).FirstOrDefaultAsync(ct);
            return producersCache[workspaceId] = (await _db.WorkspaceMembers.AsNoTracking()
                    .Where(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Producer && m.Status == WorkspaceMemberStatus.Active)
                    .Select(m => m.UserId).ToListAsync(ct))
                .Append(ownerId).Where(id => id != Guid.Empty).Distinct().ToList();
        }
        async Task Send(Domain.Entities.Workflow.Notification note)
        {
            // Nội dung cố định cho cùng một sự việc nên chống trùng bằng (người nhận, loại, chương, nội dung)
            if (await _db.Notifications.AnyAsync(n => n.UserId == note.UserId && n.Type == note.Type && n.ChapterId == note.ChapterId
                                                      && n.Message == note.Message, ct))
                return;
            _db.Notifications.Add(note);
            created++;
        }

        // Vòng lô: lô còn ô chưa xong (chưa nộp) sắp/đã quá hạn. Quá hạn thì báo cả người giao để gia hạn hoặc chia lại;
        // trễ quá escalation_hours thì báo thêm Producer/Owner (leo thang).
        var maxUpper = now + TimeSpan.FromHours(Math.Max(cmd.Lead.TotalHours, Domain.Entities.Workspaces.WorkspaceAutomationSettings.MaxHours));
        var lots = await _db.StageTaskLots.AsNoTracking()
            .Where(l => l.Status == StageTaskLotStatus.Active && l.DueDate != null && l.DueDate <= maxUpper && l.DueDate >= lower &&
                        chapterIds.Contains(l.ChapterId))
            .ToListAsync(ct);
        foreach (var lot in lots.Where(l => l.DueDate <= now + LeadOf(l.ChapterId)))
        {
            var open = await _db.StageTasks.CountAsync(t => t.LotId == lot.Id && (t.Status == StageTaskStatus.Assigned ||
                t.Status == StageTaskStatus.InProgress || t.Status == StageTaskStatus.ReworkRequired), ct);
            if (open == 0) continue;
            var overdue = lot.DueDate < now;
            Guid[] recipients = overdue ? [.. new[] { lot.AssigneeUserId, lot.AssignedByUserId }.Distinct()] : [lot.AssigneeUserId];
            foreach (var userId in recipients)
                await Send(TaskGrid.Common.TaskGridNotifications.LotDue(userId, lot, open, overdue));

            var workspaceId = workspaceOf.GetValueOrDefault(lot.ChapterId);
            if (overdue && EscalationOf(workspaceId) is { } hours && now - lot.DueDate!.Value >= TimeSpan.FromHours(hours))
                foreach (var producer in (await ProducersOf(workspaceId)).Except(recipients))
                    await Send(TaskGrid.Common.TaskGridNotifications.LotEscalated(producer, lot, hours));
        }

        // Vòng ô (bật khi Studio đặt escalation_hours): ô đã có người, đủ tiền đề nhưng quá lâu chưa bắt đầu (ready_at, N4).
        // Dữ liệu cũ có ready_at = null được bỏ qua.
        foreach (var group in chapterWorkspace.Where(c => EscalationOf(c.WorkspaceId) is not null).GroupBy(c => c.WorkspaceId))
        {
            var hours = EscalationOf(group.Key)!.Value;
            var cutoff = now - TimeSpan.FromHours(hours);
            var ids = group.Select(c => c.Id).ToList();
            var stale = await _db.StageTasks.AsNoTracking()
                .Where(t => ids.Contains(t.ChapterId) && t.ReadyAt != null && t.ReadyAt <= cutoff
                            && (t.Status == StageTaskStatus.Assigned || t.Status == StageTaskStatus.ReworkRequired))
                .GroupBy(t => t.ChapterId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            foreach (var chapter in stale)
                foreach (var producer in await ProducersOf(group.Key))
                    await Send(TaskGrid.Common.TaskGridNotifications.CellsStale(producer, chapter.Key,
                        group.First(c => c.Id == chapter.Key).ChapterNumber, hours));
        }

        // ponytail: hai instance Backend chạy cùng lúc có thể gửi trùng một lần ở cùng chu kỳ; chấp nhận được, cần thì thêm cột reminded_at + unique index
        if (created > 0) await _db.SaveChangesAsync(ct);
        return created;
    }
}
