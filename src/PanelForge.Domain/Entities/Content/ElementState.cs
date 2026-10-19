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
    public Guid? PanelId { get; set; }   // Panel chứa element (null = thuộc cả trang)
    public double Rotation { get; set; } // góc xoay, độ
    public Guid? BibleEntryId { get; set; } // mục Series Bible element thể hiện (nhân vật, đạo cụ, bối cảnh)

    // BR-05: liên kết text element ↔ ScriptLine
    public Guid? ScriptLineId { get; set; }
    public Guid? SpeakerId { get; set; }
    public string? RenderedText { get; set; }
    public string? SourceLineText { get; set; }

    // BR-20: lý do Letterer xác nhận cảnh báo tràn chữ (mất hiệu lực khi đổi chữ hoặc kích thước hộp)
    public string? OverflowAckReason { get; set; }

    // Thứ tự đọc do Letterer ghi đè (null = suy từ vị trí và hướng đọc của Series)
    public int? ReadingOrder { get; set; }

    // Kiểu chữ Letterer chọn (null = kiểu mặc định theo loại element)
    public TextStyle? Style { get; set; }

    /// <summary>
    /// TRUE = Element đã bị "xóa" (Soft Delete via ElementRemovedEvent).
    /// Không bao giờ hard delete khỏi Event Store.
    /// </summary>
    public bool IsRemoved { get; set; }

    /// <summary>BR-11: phiên bản riêng của element, tăng 1 sau mỗi event của element (bắt đầu 1 khi thêm). Chỉ báo xung đột khi cùng element.</summary>
    public int Version { get; set; }
}
