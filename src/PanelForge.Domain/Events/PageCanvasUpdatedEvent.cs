using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Events;

/// <summary>
/// Phát sinh khi cài đặt khổ giấy / canvas của Page được cập nhật.
/// </summary>
public sealed record PageCanvasUpdatedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    int WidthPx,
    int HeightPx,
    int Dpi,
    LayoutFormat LayoutFormat
) : IDomainEvent;
