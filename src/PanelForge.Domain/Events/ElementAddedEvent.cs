using PanelForge.Domain.Common;

namespace PanelForge.Domain.Events;

/// <summary>
/// Event: Một Element mới được thêm vào Panel trên Page.
/// Lưu toàn bộ thông tin khởi tạo của Element.
/// </summary>
public sealed record ElementAddedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PageId,
    Guid ElementId,
    string ElementType,
    int ZIndex,
    double X,
    double Y,
    double Width,
    double Height,
    string? Content,
    string? AssetId,
    Guid AddedByUserId
) : IDomainEvent;
