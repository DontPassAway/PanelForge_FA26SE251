using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Notifications.Queries.GetMyNotifications;

public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Title,
    string Message,
    Guid? AssignmentId,
    Guid? ChapterId,
    DateTime? ReadAt,
    DateTime CreatedAt);

public sealed record NotificationListDto(
    IReadOnlyList<NotificationDto> Items,
    int TotalCount,
    int UnreadCount,
    int PageIndex,
    int PageSize);

/// <summary>Thông báo của chính người gọi (UC-14), mới nhất trước. Không bao giờ trả thông báo của người khác.</summary>
public sealed record GetMyNotificationsQuery(Guid UserId, bool UnreadOnly = false, int PageIndex = 1, int PageSize = 20)
    : IRequest<Result<NotificationListDto>>;

public sealed class GetMyNotificationsQueryHandler : IRequestHandler<GetMyNotificationsQuery, Result<NotificationListDto>>
{
    private const int MaxPageSize = 50;
    private readonly IPanelForgeDbContext _db;

    public GetMyNotificationsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<NotificationListDto>> Handle(GetMyNotificationsQuery q, CancellationToken ct)
    {
        var pageIndex = Math.Max(1, q.PageIndex);
        var pageSize = Math.Clamp(q.PageSize, 1, MaxPageSize);

        var mine = _db.Notifications.AsNoTracking().Where(n => n.UserId == q.UserId);
        var unreadCount = await mine.CountAsync(n => n.ReadAt == null, ct);

        var scoped = q.UnreadOnly ? mine.Where(n => n.ReadAt == null) : mine;
        var total = await scoped.CountAsync(ct);

        var rows = await scoped
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var items = rows.Select(n => new NotificationDto(
            n.Id, n.Type, n.Title, n.Message, n.AssignmentId, n.ChapterId, n.ReadAt, n.CreatedAt)).ToList();

        return Result<NotificationListDto>.Success(new NotificationListDto(items, total, unreadCount, pageIndex, pageSize));
    }
}
