namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>Panel tìm được trên ảnh trang. Bbox = [x, y, width, height] theo pixel ảnh gốc; Order = thứ tự đọc (1-based).</summary>
public sealed record SplitPanelDto(int Order, int[] Bbox);

/// <summary>Bong bóng thoại tìm được. PanelOrder trỏ tới <see cref="SplitPanelDto.Order"/>; Polygon = các điểm [x, y].</summary>
public sealed record SplitBubbleDto(int PanelOrder, int[] Bbox, int[][] Polygon);

public sealed record PanelSplitResult(int Width, int Height, IReadOnlyList<SplitPanelDto> Panels, IReadOnlyList<SplitBubbleDto> Bubbles);

/// <summary>
/// Gợi ý tách panel + bong bóng từ ảnh cả trang (service Python PanelForge.PanelSplitter).
/// Chỉ trả gợi ý, không lưu gì: người dùng xác nhận trước khi áp vào layout.
/// </summary>
public interface IPanelSplitter
{
    /// <exception cref="InvalidOperationException">Service không chạy hoặc trả lỗi.</exception>
    Task<PanelSplitResult> SplitAsync(Stream image, string fileName, bool rightToLeft, CancellationToken cancellationToken = default);
}
