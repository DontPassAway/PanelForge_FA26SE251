using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.TypographyPresets.Commands;

/// <summary>DTO for TypographyPreset responses.</summary>
public sealed record TypographyPresetDto(
    Guid Id,
    Guid SeriesId,
    string Name,
    string FontFamily,
    int FontSize,
    int FontWeight,
    string FontStyle,
    decimal LineHeight,
    decimal LetterSpacing,
    string TextAlign,
    string UsageType,
    bool IsDefault,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Extension mapping from entity to DTO.</summary>
public static class TypographyPresetExtensions
{
    public static TypographyPresetDto ToDto(this TypographyPreset preset) => new(
        Id: preset.Id,
        SeriesId: preset.SeriesId,
        Name: preset.Name,
        FontFamily: preset.FontFamily,
        FontSize: preset.FontSize,
        FontWeight: preset.FontWeight,
        FontStyle: preset.FontStyle,
        LineHeight: preset.LineHeight,
        LetterSpacing: preset.LetterSpacing,
        TextAlign: preset.TextAlign,
        UsageType: preset.UsageType,
        IsDefault: preset.IsDefault,
        CreatedAt: preset.CreatedAt,
        UpdatedAt: preset.UpdatedAt);
}
