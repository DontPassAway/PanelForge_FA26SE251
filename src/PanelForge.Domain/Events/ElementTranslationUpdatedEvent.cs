using PanelForge.Domain.Common;

namespace PanelForge.Domain.Events;

/// <summary>
/// Event: cập nhật bản dịch của một Element cho một ngôn ngữ phát hành (BR-34).
/// Tăng Version của element (BR-11, optimistic concurrency).
/// </summary>
public sealed record ElementTranslationUpdatedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid ElementId,
    string LanguageCode,
    string? Text,
    string? PreviousText,
    Guid UpdatedByUserId
) : IDomainEvent;
