using PanelForge.Domain.Common;

namespace PanelForge.Domain.Events;

/// <summary>BR-20: Letterer xác nhận cảnh báo tràn chữ kèm lý do để Lettering qua được cổng thay vì bị chặn.</summary>
public sealed record ElementOverflowAcknowledgedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid ElementId,
    string Reason,
    Guid AcknowledgedByUserId
) : IDomainEvent;
