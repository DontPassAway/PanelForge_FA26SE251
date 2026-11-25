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

    /// <summary>
    /// Ghép trang từ ảnh từng Panel (cùng quy tắc FE `compositePageRenderer`): nền trắng → mỗi Panel ảnh co "contain" trong
    /// hộp khung, clip theo khung (đa giác khi có corners) → viền khung trừ khi borderless. Ảnh lỗi thành ô giữ chỗ. Trả PNG khổ trang.
    /// </summary>
    ExportPageImage ComposePanels(int pageNumber, int pageWidthPx, int pageHeightPx, IReadOnlyList<PagePanelLayer> panels);
}

/// <summary>Một Panel cần ghép: ảnh (null = chỉ vẽ khung), hộp khung px trên trang, góc đa giác 0–1 trong hộp (tuỳ chọn).</summary>
public sealed record PagePanelLayer(
    byte[]? Content, double X, double Y, double Width, double Height,
    IReadOnlyList<(double X, double Y)>? Corners = null, bool Borderless = false);
