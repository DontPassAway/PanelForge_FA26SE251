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
/// </summary>
public sealed record SendAssignmentRemindersCommand(TimeSpan Lead, DateTime? NowUtc = null) : IRequest<int>;

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

        var candidates = await _db.Assignments.AsNoTracking()
            .Where(a => a.DueDate != null && a.DueDate <= upper && a.DueDate >= lower && OpenStatuses.Contains(a.Status) &&
                        _db.Chapters.Any(c => c.Id == a.ChapterId && c.Status == ChapterStatus.InProduction))
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
                         _db.Chapters.Any(c => c.Id == cr.ChapterId && c.Status == ChapterStatus.InProduction))
            .ToListAsync(ct);

        foreach (var cr in crCandidates)
        {
            var note = cr.DueDate < now ? AssignmentNotifications.ChangeRequestOverdue(cr) : AssignmentNotifications.ChangeRequestDueSoon(cr);

            var alreadySent = await _db.Notifications.AnyAsync(n =>
                n.AssignmentId == cr.AssignmentId && n.UserId == cr.ResponsibleUserId && n.Type == note.Type && n.Message == note.Message, ct);
            if (alreadySent) continue;

            _db.Notifications.Add(note);
            created++;
        }

        // ponytail: hai instance Backend chạy cùng lúc có thể gửi trùng một lần ở cùng chu kỳ; chấp nhận được, cần thì thêm cột reminded_at + unique index
        if (created > 0) await _db.SaveChangesAsync(ct);
        return created;
    }
}
