using PanelForge.Domain.Common;

namespace PanelForge.Domain.Events;

/// <summary>
/// Event: chữ của một Element đổi (Letterer sửa chữ, làm mới sau khi Writer sửa thoại, hoặc gắn lại sang ScriptLine khác).
/// Lưu cả chữ cũ và liên kết cũ để audit/undo. Làm mất hiệu lực xác nhận tràn chữ trước đó.
/// </summary>
public sealed record ElementTextUpdatedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid ElementId,
    string? Content,             // chữ mới (cũng là RenderedText khi có ScriptLineId)
    Guid? ScriptLineId,          // liên kết sau khi sửa (BR-05)
    Guid? SpeakerId,
    string? SourceLineText,      // thoại gốc tại thời điểm gắn
    string? PreviousContent,
    Guid? PreviousScriptLineId,
    Guid UpdatedByUserId
) : IDomainEvent;
