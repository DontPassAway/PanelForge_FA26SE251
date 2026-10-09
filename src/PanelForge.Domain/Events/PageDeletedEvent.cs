using PanelForge.Domain.Common;

namespace PanelForge.Domain.Events;

/// <summary>
/// Phát sinh khi Page bị xóa mềm. Stream vẫn được giữ nguyên (append-only, BR-01);
/// sau event này Page không còn nhận thêm thao tác nào.
/// </summary>
public sealed record PageDeletedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid? DeletedByUserId
) : IDomainEvent;
