
namespace PanelForge.Application.Features.ConsistencyRules.Models;

public sealed record ConsistencyRuleDto(
    Guid Id,
    Guid SeriesId,
    string Name,
    string RuleType,
    string? Description,
    bool IsEnabled,
    string? Pattern,
    string Severity,
    string ConfigurationJson,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
