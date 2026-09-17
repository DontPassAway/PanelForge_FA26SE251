using PanelForge.Domain.Common;

namespace PanelForge.Domain.Events;

/// <summary>
/// Phát sinh khi thứ tự trang (PageNumber) trong chapter bị thay đổi.
/// </summary>
public sealed record PageReorderedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    int NewPageNumber
) : IDomainEvent;
