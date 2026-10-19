using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>Một element chữ của trang cần vẽ vào bản xuất (toạ độ thô như lưu trong event store).</summary>
public sealed record ExportLetteringElement(
    string ElementType, double X, double Y, double Width, double Height, double Rotation, int ZIndex,
    string Text, TextStyle? Style);

/// <summary>
/// Vẽ lớp chữ (bóng thoại, dẫn truyện, SFX) lên ảnh trang theo đúng quy tắc của bàn Letterer / màn duyệt
/// (FE `roles/letterer/lib/balloonRender.ts`): hình bóng, nền, viền, vùng chữ an toàn, ngắt dòng, cỡ chữ không tự thu nhỏ.
/// Là port vì cần thư viện vẽ ở hạ tầng.
/// </summary>
public interface IPageLetteringCompositor
{
    /// <summary>Trả ảnh trang (PNG) đã có lớp chữ; không có element nào thì trả nguyên <paramref name="page"/>.</summary>
    ExportPageImage Compose(ExportPageImage page, int pageWidthPx, int pageHeightPx, IReadOnlyList<ExportLetteringElement> elements);
}
