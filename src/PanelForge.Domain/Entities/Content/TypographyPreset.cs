using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// E-31: Series-level TypographyPreset entity (Task 8).
/// Replaces TypographyPresetsJson blob in SeriesPreset.
/// </summary>
public class TypographyPreset : BaseEntity
{
    public Guid SeriesId { get; private set; }
    public string Name { get; private set; } = default!;
    public string FontFamily { get; private set; } = default!;
    public int FontSize { get; private set; }
    public int FontWeight { get; private set; } = 400;
    public string FontStyle { get; private set; } = "Normal";
    public decimal LineHeight { get; private set; } = 1.2m;
    public decimal LetterSpacing { get; private set; } = 0m;
    public string TextAlign { get; private set; } = "Left";

    /// <summary>
    /// UsageType: "Dialogue" | "Narration" | "Thought" | "SoundEffect" | "Caption" | "Custom"
    /// </summary>
    public string UsageType { get; private set; } = "Custom";
    public bool IsDefault { get; private set; } = false;

    public Series Series { get; private set; } = default!;

    private TypographyPreset() { }

    public static TypographyPreset Create(
        Guid seriesId,
        string name,
        string fontFamily,
        int fontSize,
        int fontWeight = 400,
        string fontStyle = "Normal",
        decimal lineHeight = 1.2m,
        decimal letterSpacing = 0m,
        string textAlign = "Left",
        string usageType = "Custom",
        bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        if (fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize), "FontSize must be > 0.");

        return new TypographyPreset
        {
            SeriesId = seriesId,
            Name = name.Trim(),
            FontFamily = fontFamily.Trim(),
            FontSize = fontSize,
            FontWeight = fontWeight,
            FontStyle = fontStyle.Trim(),
            LineHeight = lineHeight,
            LetterSpacing = letterSpacing,
            TextAlign = textAlign.Trim(),
            UsageType = usageType.Trim(),
            IsDefault = isDefault,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(
        string name, string fontFamily, int fontSize,
        int fontWeight, string fontStyle, decimal lineHeight,
        decimal letterSpacing, string textAlign, string usageType, bool isDefault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        if (fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize), "FontSize must be > 0.");
        Name = name.Trim();
        FontFamily = fontFamily.Trim();
        FontSize = fontSize;
        FontWeight = fontWeight;
        FontStyle = fontStyle.Trim();
        LineHeight = lineHeight;
        LetterSpacing = letterSpacing;
        TextAlign = textAlign.Trim();
        UsageType = usageType.Trim();
        IsDefault = isDefault;
        UpdatedAt = DateTime.UtcNow;
    }
}
