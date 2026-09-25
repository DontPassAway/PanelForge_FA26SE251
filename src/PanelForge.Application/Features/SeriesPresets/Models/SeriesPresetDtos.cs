namespace PanelForge.Application.Features.SeriesPresets.Models;

public sealed record SeriesPresetDto(
    Guid Id,
    Guid SeriesId,
    string TypographyPresetsJson,
    string ConsistencyRulesJson,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public sealed record UpdateSeriesPresetRequest(
    string TypographyPresetsJson,
    string ConsistencyRulesJson
);
