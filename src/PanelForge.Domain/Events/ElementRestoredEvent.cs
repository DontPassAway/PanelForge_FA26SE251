using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Domain.Events;

/// <summary>
/// CF4 4.3 / US-12 (BR-01, BR-08): khôi phục element về trạng thái ở một version cũ của Page. Mang đủ trạng thái để dựng lại
/// (kể cả element đã xoá mềm), không xoá hay sửa event nào trước đó.
/// </summary>
public sealed record ElementRestoredEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid ElementId,
    long RestoredFromVersion,
    string ElementType,
    int ZIndex,
    double X,
    double Y,
    double Width,
    double Height,
    double Rotation,
    string? Content,
    string? AssetId,
    Guid? PanelId,
    Guid? BibleEntryId,
    Guid? ScriptLineId,
    Guid? SpeakerId,
    string? RenderedText,
    string? SourceLineText,
    string? OverflowAckReason,
    int? ReadingOrder,
    TextStyle? Style,
    Guid RestoredByUserId
) : IDomainEvent;
