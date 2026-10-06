using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Notifications.Commands.MarkNotificationsRead;

/// <summary>Đánh dấu đã đọc một thông báo (NotificationId) hoặc tất cả của người gọi (NotificationId = null). Trả số thông báo vừa đổi.</summary>
public sealed record MarkNotificationsReadCommand(Guid UserId, Guid? NotificationId = null) : IRequest<Result<int>>;

public sealed class MarkNotificationsReadCommandHandler : IRequestHandler<MarkNotificationsReadCommand, Result<int>>
{
    private readonly IPanelForgeDbContext _db;

    public MarkNotificationsReadCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<int>> Handle(MarkNotificationsReadCommand cmd, CancellationToken ct)
    {
        // Luôn lọc theo UserId: thông báo của người khác coi như không tồn tại
        var query = _db.Notifications.Where(n => n.UserId == cmd.UserId && n.ReadAt == null);
        if (cmd.NotificationId is { } id)
        {
            var exists = await _db.Notifications.AnyAsync(n => n.Id == id && n.UserId == cmd.UserId, ct);
            if (!exists)
                return Result<int>.Failure($"Thông báo {id} không tồn tại.", ResultErrorCodes.NotFound);

            query = query.Where(n => n.Id == id);
        }

        var unread = await query.ToListAsync(ct);
        foreach (var n in unread) n.MarkRead();

        if (unread.Count > 0) await _db.SaveChangesAsync(ct);
        return Result<int>.Success(unread.Count);
    }
}
