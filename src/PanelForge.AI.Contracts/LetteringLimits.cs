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

    /// <summary>Cỡ nhỏ nhất khi tự thu nhỏ (= TextStyle.MinFontSize).</summary>
    public const double MinFontPx = 14;

    /// <summary>Phần lòng elip chữ thực sự lấp được khi xếp dòng (dòng trên/dưới ngắn).</summary>
    private const double EllipseFill = 0.8;

    /// <summary>Hình bóng mặc định theo loại element (FE defaultAppearance).</summary>
    public static string DefaultShape(string elementType)
    {
        var t = elementType.ToUpperInvariant();
        return t.Contains("NARRATION") || t.Contains("CAPTION") ? "box"
            : t.Contains("SFX") || t.Contains("SOUND") ? "spiky"
            : t.Contains("THOUGHT") ? "cloud"
            : "oval";
    }

    /// <summary>Số ký tự ước lượng vừa bóng; shape cong dùng lòng elip như FE innerEllipse.</summary>
    public static int ForElement(
        double widthPx, double heightPx, string? shape = null, double fontPx = FontPx, double lineHeightEm = LineHeightEm)
    {
        var charW = fontPx * CharWidthEm;
        var lineH = fontPx * lineHeightEm;
        if (shape is "oval" or "cloud" or "spiky")
        {
            var k = shape == "spiky" ? 0.74 : 0.9;
            var rx = Math.Max(0, widthPx / 2 * k - 4);
            var ry = Math.Max(0, heightPx / 2 * k - 4);
            var chars = Math.PI * rx * ry * EllipseFill / (charW * lineH);
            return Math.Max(MinChars, (int)Math.Floor(chars));
        }

        var w = Math.Max(0, widthPx - 2 * PaddingPx);
        var h = Math.Max(0, heightPx - 2 * PaddingPx);
        var perLine = (int)Math.Floor(w / charW);
        var lines = (int)Math.Floor(h / lineH);
        return Math.Max(MinChars, perLine * lines);
    }

    public static int ForPanel(double panelWidthPx, double panelHeightPx)
    {
        var area = Math.Max(0, panelWidthPx) * Math.Max(0, panelHeightPx) * PanelBalloonShare;
        return Math.Max(MinChars, (int)Math.Floor(area / (FontPx * CharWidthEm * FontPx * LineHeightEm)));
    }
}
