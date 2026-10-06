using PanelForge.Domain.Common;

namespace PanelForge.Domain.Events;

/// <summary>Letterer ghi đè thứ tự đọc của một text element bằng số thứ tự riêng (null = bỏ ghi đè, quay về suy từ vị trí).</summary>
public sealed record ElementReadingOrderSetEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid ElementId,
    int? ReadingOrder,
    int? PreviousReadingOrder,
    Guid SetByUserId
) : IDomainEvent;
