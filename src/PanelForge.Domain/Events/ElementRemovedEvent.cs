using PanelForge.Domain.Common;

namespace PanelForge.Domain.Events;

/// <summary>
/// Event: Element bị "xóa" bằng Soft Delete.
/// Event được APPEND vào stream — Event Store không bao giờ xóa record nào.
/// Khi Apply event này, chỉ đánh dấu ElementState.IsRemoved = true.
/// </summary>
public sealed record ElementRemovedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid ElementId,
    string Reason,
    Guid RemovedByUserId
) : IDomainEvent;
