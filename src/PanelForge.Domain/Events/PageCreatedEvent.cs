using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Events;

/// <summary>
/// Phát sinh khi một Trang truyện (Page) mới được khởi tạo.
/// Lưu vào Marten stream của Page làm event đầu tiên (Version = 1).
/// </summary>
public sealed record PageCreatedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid ChapterId,
    int PageNumber,
    LayoutFormat LayoutFormat,
    int WidthPx,
    int HeightPx,
    int Dpi,
    Guid? SceneId
) : IDomainEvent;
