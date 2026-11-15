using PanelForge.Domain.Common;

namespace PanelForge.Domain.Events;

public sealed record ElementPanelReassignedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid ElementId,
    Guid? PanelId,
    Guid? PreviousPanelId,
    Guid ReassignedByUserId
) : IDomainEvent;
