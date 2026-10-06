namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Trạng thái in-memory của một Element bên trong Page aggregate.
/// Được xây dựng bằng cách Apply các events — không persist trực tiếp.
/// </summary>
public sealed class ElementState
{
    public Guid ElementId { get; set; }
    public string ElementType { get; set; } = default!;
    public int ZIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? Content { get; set; }  // Text nội dung (balloon/narration)
    public string? AssetId { get; set; } // Asset ID (artwork layer)

    // BR-05: liên kết text element ↔ ScriptLine
    public Guid? ScriptLineId { get; set; }
    public Guid? SpeakerId { get; set; }
    public string? RenderedText { get; set; }
    public string? SourceLineText { get; set; }

    // BR-20: lý do Letterer xác nhận cảnh báo tràn chữ (mất hiệu lực khi đổi chữ hoặc kích thước hộp)
    public string? OverflowAckReason { get; set; }

    // Thứ tự đọc do Letterer ghi đè (null = suy từ vị trí và hướng đọc của Series)
    public int? ReadingOrder { get; set; }

    /// <summary>
    /// TRUE = Element đã bị "xóa" (Soft Delete via ElementRemovedEvent).
    /// Không bao giờ hard delete khỏi Event Store.
    /// </summary>
    public bool IsRemoved { get; set; }
}
