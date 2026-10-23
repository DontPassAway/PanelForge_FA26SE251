using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Common;
using PanelForge.Domain.Events;

namespace PanelForge.Application.Features.Pages.Queries.GetPageHistory;

/// <summary>
/// CF4 4.3 (UC-18, BR-01): lịch sử canvas của một Page = các event Marten của stream, mới nhất trước, có phân trang.
/// Version = vị trí event trong stream (1 = PageCreated); dùng làm tham số cho xem/so sánh/khôi phục.
/// Quyền thành viên Workspace kiểm ở controller (BR-07). Chỉ đọc.
/// </summary>
public sealed record GetPageHistoryQuery(Guid PageId, int Page = 1, int PageSize = 50) : IRequest<Result<PageHistoryDto>>;

/// <param name="EventType">Tên event (ElementAdded, ElementMoved, ElementRestored, ...), bỏ hậu tố "Event".</param>
/// <param name="ActorUserId">Người thực hiện; null với event hệ thống (tạo trang, đổi khổ, đổi số trang).</param>
public sealed record PageHistoryEntryDto(
    long Version, string EventType, Guid? ElementId, Guid? ActorUserId, string? ActorName, DateTime OccurredAtUtc, string Summary);

public sealed record PageHistoryDto(
    Guid PageId, long CurrentVersion, int Page, int PageSize, int TotalCount, IReadOnlyList<PageHistoryEntryDto> Items);

public sealed class GetPageHistoryQueryHandler : IRequestHandler<GetPageHistoryQuery, Result<PageHistoryDto>>
{
    private const int MaxPageSize = 100;

    private readonly IPageRepository _pages;
    private readonly IPanelForgeDbContext _db;

    public GetPageHistoryQueryHandler(IPageRepository pages, IPanelForgeDbContext db)
    {
        _pages = pages;
        _db = db;
    }

    public async Task<Result<PageHistoryDto>> Handle(GetPageHistoryQuery q, CancellationToken ct)
    {
        var events = await _pages.GetEventsAsync(q.PageId, ct);
        if (events.Count == 0)
            return Result<PageHistoryDto>.Failure($"Page {q.PageId} không tồn tại.", ResultErrorCodes.NotFound);

        var pageSize = Math.Clamp(q.PageSize, 1, MaxPageSize);
        var pageNumber = Math.Max(q.Page, 1);

        // Loại element theo id để mô tả event không mang loại (di chuyển, xoá...)
        var types = new Dictionary<Guid, string>();
        var all = new List<(long Version, IDomainEvent Event, Described D)>(events.Count);
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is ElementAddedEvent added) types[added.ElementId] = added.ElementType;
            if (events[i] is ElementRestoredEvent restored) types[restored.ElementId] = restored.ElementType;
            all.Add((i + 1, events[i], Describe(events[i], types)));
        }

        var items = all.OrderByDescending(x => x.Version).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        var userIds = items.Where(x => x.D.Actor.HasValue).Select(x => x.D.Actor!.Value).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return Result<PageHistoryDto>.Success(new PageHistoryDto(
            q.PageId, events.Count, pageNumber, pageSize, events.Count,
            items.Select(x => new PageHistoryEntryDto(
                x.Version, EventName(x.Event), x.D.ElementId, x.D.Actor,
                x.D.Actor is { } id ? names.GetValueOrDefault(id) : null,
                x.Event.OccurredAt.UtcDateTime, x.D.Summary)).ToList()));
    }

    private sealed record Described(Guid? ElementId, Guid? Actor, string Summary);

    private static string EventName(IDomainEvent e)
    {
        var name = e.GetType().Name;
        return name.EndsWith("Event") ? name[..^"Event".Length] : name;
    }

    private static Described Describe(IDomainEvent e, IReadOnlyDictionary<Guid, string> types)
    {
        string Type(Guid id) => types.GetValueOrDefault(id) ?? "element";
        return e switch
        {
            PageCreatedEvent x => new(null, null, $"Tạo trang {x.PageNumber} ({x.WidthPx}x{x.HeightPx}, {x.Dpi} dpi)"),
            PageCanvasUpdatedEvent x => new(null, null, $"Đổi khổ canvas {x.WidthPx}x{x.HeightPx}, {x.Dpi} dpi"),
            PageReorderedEvent x => new(null, null, $"Đổi số trang thành {x.NewPageNumber}"),
            PageDeletedEvent x => new(null, x.DeletedByUserId, "Xoá trang"),
            ElementAddedEvent x => new(x.ElementId, x.AddedByUserId, $"Thêm {x.ElementType}"),
            ElementMovedEvent x => new(x.ElementId, x.MovedByUserId, $"Di chuyển / đổi cỡ {Type(x.ElementId)}"),
            ElementRemovedEvent x => new(x.ElementId, x.RemovedByUserId, $"Xoá {Type(x.ElementId)} ({x.Reason})"),
            ElementTextUpdatedEvent x => new(x.ElementId, x.UpdatedByUserId, $"Sửa chữ {Type(x.ElementId)}"),
            ElementStyleUpdatedEvent x => new(x.ElementId, x.UpdatedByUserId, $"Đổi kiểu chữ {Type(x.ElementId)}"),
            ElementReadingOrderSetEvent x => new(x.ElementId, x.SetByUserId,
                $"Thứ tự đọc {Type(x.ElementId)}: {x.ReadingOrder?.ToString() ?? "bỏ ghi đè"}"),
            ElementOverflowAcknowledgedEvent x => new(x.ElementId, x.AcknowledgedByUserId, $"Xác nhận tràn chữ: {x.Reason}"),
            ElementRestoredEvent x => new(x.ElementId, x.RestoredByUserId,
                $"Khôi phục {x.ElementType} về bản v{x.RestoredFromVersion}"),
            _ => new(null, null, e.GetType().Name)
        };
    }
}
