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
    Guid AddedByUserId,
    // BR-05: text element gắn đúng một ScriptLine. Null ở event cũ / element không phải chữ.
    Guid? ScriptLineId = null,
    Guid? SpeakerId = null,
    string? RenderedText = null,    // chữ thực sự vẽ lên element (Letterer có thể rút gọn)
    string? SourceLineText = null   // thoại gốc lúc gắn, để phát hiện chữ lỗi thời khi Writer sửa
) : IDomainEvent;
