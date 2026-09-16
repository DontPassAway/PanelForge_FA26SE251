using PanelForge.Domain.Common;

namespace PanelForge.Domain.Events;

/// <summary>
/// Event: Vị trí hoặc kích thước của Element thay đổi.
/// Lưu cả vị trí CŨ để có thể Undo/Diff về sau.
/// </summary>
public sealed record ElementMovedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid ElementId,
    double PreviousX,
    double PreviousY,
    double NewX,
    double NewY,
    double NewWidth,
    double NewHeight,
    Guid MovedByUserId
) : IDomainEvent;
