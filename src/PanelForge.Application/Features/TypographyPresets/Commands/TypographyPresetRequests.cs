namespace PanelForge.Application.Features.TypographyPresets.Commands;

/// <summary>HTTP request model for creating a TypographyPreset.</summary>
public sealed record CreateTypographyPresetRequest(
    string Name,
    string FontFamily,
    int FontSize,
    int FontWeight = 400,
    string FontStyle = "Normal",
    decimal LineHeight = 1.2m,
    decimal LetterSpacing = 0m,
    string TextAlign = "Left",
    string UsageType = "Custom",
    bool IsDefault = false);

/// <summary>HTTP request model for updating a TypographyPreset.</summary>
public sealed record UpdateTypographyPresetRequest(
    string Name,
    string FontFamily,
    int FontSize,
    int FontWeight = 400,
    string FontStyle = "Normal",
    decimal LineHeight = 1.2m,
    decimal LetterSpacing = 0m,
    string TextAlign = "Left",
    string UsageType = "Custom",
    bool IsDefault = false);
