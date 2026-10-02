namespace PanelForge.AI.Contracts;

/// <summary>
/// Giới hạn chữ cho lettering do CODE ước lượng từ kích thước hộp chữ (px trên canvas Page). Đây là ƯỚC LƯỢNG
/// (chưa biết font thật, ngắt dòng, hình dạng bubble), dùng để định hướng và chặn đề xuất quá dài —
/// KHÔNG phải kết luận chữ chắc chắn vừa bubble. Người Letterer vẫn kiểm tra khi đặt chữ lên canvas.
/// </summary>
public static class LetteringLimits
{
    public const string SourceElement = "element-estimate";   // theo kích thước bubble/text element trên Page
    public const string SourcePanel = "panel-estimate";       // chưa có bubble: theo diện tích panel
    public const double FontPx = 26;
    public const double CharWidthEm = 0.55;    // chữ Latin/Việt trung bình
    public const double LineHeightEm = 1.3;
    public const double PaddingPx = 12;
    public const double PanelBalloonShare = 0.12;  // bubble thường chiếm ≲ 12% diện tích panel
    public const int MinChars = 8;

    public static int ForElement(double widthPx, double heightPx)
    {
        var w = Math.Max(0, widthPx - 2 * PaddingPx);
        var h = Math.Max(0, heightPx - 2 * PaddingPx);
        var perLine = (int)Math.Floor(w / (FontPx * CharWidthEm));
        var lines = (int)Math.Floor(h / (FontPx * LineHeightEm));
        return Math.Max(MinChars, perLine * lines);
    }

    public static int ForPanel(double panelWidthPx, double panelHeightPx)
    {
        var area = Math.Max(0, panelWidthPx) * Math.Max(0, panelHeightPx) * PanelBalloonShare;
        return Math.Max(MinChars, (int)Math.Floor(area / (FontPx * CharWidthEm * FontPx * LineHeightEm)));
    }
}
