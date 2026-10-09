using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Cấu hình Typography Presets và Consistency Rules cho Series (NFR-08).
/// Thay đổi linh hoạt qua giao diện không cần redeploy.
/// </summary>
public class SeriesPreset : BaseEntity
{
    public Guid SeriesId { get; private set; }
    public string TypographyPresetsJson { get; private set; } = "{}";
    public string ConsistencyRulesJson { get; private set; } = "{}";

    public Series Series { get; private set; } = default!;

    private SeriesPreset() { }

    public static SeriesPreset Create(
        Guid seriesId,
        string? typographyPresetsJson = null,
        string? consistencyRulesJson = null)
    {
        return new SeriesPreset
        {
            SeriesId = seriesId,
            TypographyPresetsJson = string.IsNullOrWhiteSpace(typographyPresetsJson)
                ? GetDefaultTypographyPresets()
                : typographyPresetsJson.Trim(),
            ConsistencyRulesJson = string.IsNullOrWhiteSpace(consistencyRulesJson)
                ? GetDefaultConsistencyRules()
                : consistencyRulesJson.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdatePresets(string? typographyPresetsJson, string? consistencyRulesJson)
    {
        if (!string.IsNullOrWhiteSpace(typographyPresetsJson))
        {
            TypographyPresetsJson = typographyPresetsJson.Trim();
        }
        if (!string.IsNullOrWhiteSpace(consistencyRulesJson))
        {
            ConsistencyRulesJson = consistencyRulesJson.Trim();
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public static string GetDefaultTypographyPresets() =>
        """
        {
            "dialogue": { "fontFamily": "Anime Ace 3", "fontSize": 14, "lineHeight": 1.2, "fontWeight": "normal" },
            "narration": { "fontFamily": "Wild Words", "fontSize": 12, "lineHeight": 1.3, "fontStyle": "italic" },
            "thought": { "fontFamily": "CC Spinechiller", "fontSize": 13, "lineHeight": 1.2, "fontStyle": "italic" },
            "sfx": { "fontFamily": "Komika Axis", "fontSize": 24, "fontWeight": "bold", "strokeWidth": 2 }
        }
        """;

    public static string GetDefaultConsistencyRules() =>
        """
        {
            "characterContinuity": true,
            "propContinuity": true,
            "loreContinuity": true,
            "toneConsistency": true,
            "strictSpelling": true,
            "warnDialogueOverflow": true
        }
        """;
}
