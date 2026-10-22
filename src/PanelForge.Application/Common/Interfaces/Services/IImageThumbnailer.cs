namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>
/// Thu nhỏ ảnh artwork để hiển thị dạng lưới/danh sách. Là port vì giải mã ảnh cần thư viện native (SkiaSharp) ở hạ tầng.
/// </summary>
public interface IImageThumbnailer
{
    /// <summary>Trả ảnh WebP có cạnh rộng tối đa <paramref name="maxWidth"/>; null nếu nội dung không giải mã được thành ảnh.</summary>
    byte[]? CreateWebp(Stream source, int maxWidth);
}
