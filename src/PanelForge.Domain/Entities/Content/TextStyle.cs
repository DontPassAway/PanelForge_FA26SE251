using System.Text.RegularExpressions;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Kiểu chữ của một element chữ (bóng thoại, dẫn truyện, SFX). Cố ý gọn hơn Word: chỉ vài font manga có dấu tiếng Việt,
/// cỡ chữ có mức tối thiểu để in vẫn đọc được. <see cref="Preset"/> chỉ là nhãn kiểu có sẵn Letterer đã chọn (FE định nghĩa).
/// </summary>
public sealed record TextStyle(
    string FontFamily,
    int FontSize,                 // px trên khổ trang (vd 1200×1800)
    bool Bold = false,
    bool Italic = false,
    string Align = "center",      // center | left | right
    string Color = "#0A0A0A",
    string? StrokeColor = null,   // viền chữ (SFX, chữ trên nền tối)
    int StrokeWidth = 0,
    double LineHeight = 1.2,
    string? Preset = null,
    // Hình bóng chứa chữ (độc lập với loại element). null = mặc định theo loại (FE `defaultAppearance`):
    // SPEECH oval trắng viền đen, THOUGHT mây, NARRATION hộp kem, SFX răng cưa đỏ. Event cũ không có các trường này → null.
    string? BalloonShape = null,  // none | oval | cloud | spiky | box ("none" = chỉ chữ, đặt vào bóng Artist đã vẽ)
    string? FillColor = null,     // #RRGGBB
    double? FillOpacity = null,   // 0..1
    string? BorderColor = null,   // #RRGGBB
    double? BorderWidth = null,   // px trên khổ trang, 0 = không viền
    double? TailX = null,         // chóp đuôi bóng, px trang tính từ góc trên-trái của bóng (có thể âm / vượt bóng)
    double? TailY = null)
{
    public static readonly IReadOnlySet<string> AllowedFonts = new HashSet<string>
    {
        "Be Vietnam Pro", "Mali", "Patrick Hand", "Bangers"
    };

    public static readonly IReadOnlySet<string> AllowedAligns = new HashSet<string> { "center", "left", "right" };

    public static readonly IReadOnlySet<string> AllowedBalloonShapes = new HashSet<string>
    {
        "none", "oval", "cloud", "spiky", "box"
    };

    /// <summary>Đuôi bóng không xa quá một khổ trang so với bóng.</summary>
    public const double MaxTailOffset = 5000;

    public const int MinFontSize = 14;
    public const int MaxFontSize = 160;

    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    /// <summary>Ném <see cref="ArgumentException"/> nếu kiểu chữ không hợp lệ.</summary>
    public TextStyle Validated()
    {
        if (!AllowedFonts.Contains(FontFamily))
            throw new ArgumentException($"Font '{FontFamily}' không nằm trong danh sách: {string.Join(", ", AllowedFonts)}.");
        if (FontSize is < MinFontSize or > MaxFontSize)
            throw new ArgumentException($"Cỡ chữ phải từ {MinFontSize} đến {MaxFontSize}px (chữ nhỏ hơn không đọc được khi in).");
        if (!AllowedAligns.Contains(Align))
            throw new ArgumentException("Căn lề chỉ nhận center, left hoặc right.");
        if (!HexColor.IsMatch(Color))
            throw new ArgumentException("Màu chữ phải dạng #RRGGBB.");
        if (StrokeColor is not null && !HexColor.IsMatch(StrokeColor))
            throw new ArgumentException("Màu viền chữ phải dạng #RRGGBB.");
        if (StrokeWidth is < 0 or > 12)
            throw new ArgumentException("Độ dày viền chữ phải từ 0 đến 12px.");
        if (LineHeight is < 0.8 or > 2.5)
            throw new ArgumentException("Giãn dòng phải từ 0.8 đến 2.5.");
        if (Preset is { Length: > 50 })
            throw new ArgumentException("Tên kiểu có sẵn tối đa 50 ký tự.");
        if (BalloonShape is not null && !AllowedBalloonShapes.Contains(BalloonShape))
            throw new ArgumentException($"Hình bóng chỉ nhận: {string.Join(", ", AllowedBalloonShapes)}.");
        if (FillColor is not null && !HexColor.IsMatch(FillColor))
            throw new ArgumentException("Màu nền bóng phải dạng #RRGGBB.");
        if (FillOpacity is < 0 or > 1)
            throw new ArgumentException("Độ đục nền bóng phải từ 0 đến 1.");
        if (BorderColor is not null && !HexColor.IsMatch(BorderColor))
            throw new ArgumentException("Màu viền bóng phải dạng #RRGGBB.");
        if (BorderWidth is < 0 or > 12)
            throw new ArgumentException("Độ dày viền bóng phải từ 0 đến 12px.");
        if (TailX is < -MaxTailOffset or > MaxTailOffset || TailY is < -MaxTailOffset or > MaxTailOffset)
            throw new ArgumentException($"Toạ độ đuôi bóng phải trong khoảng ±{MaxTailOffset}px.");
        return this;
    }
}
