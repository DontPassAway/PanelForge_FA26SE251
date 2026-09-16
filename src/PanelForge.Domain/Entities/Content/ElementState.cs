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

    /// <summary>
    /// TRUE = Element đã bị "xóa" (Soft Delete via ElementRemovedEvent).
    /// Không bao giờ hard delete khỏi Event Store.
    /// </summary>
    public bool IsRemoved { get; set; }
}
