using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Domain.Events;

/// <summary>Letterer đổi kiểu chữ của một element chữ (null = về kiểu mặc định). Giữ kiểu cũ để truy vết (BR-01).</summary>
public sealed record ElementStyleUpdatedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid ElementId,
    TextStyle? Style,
    TextStyle? PreviousStyle,
    Guid UpdatedByUserId
) : IDomainEvent;
