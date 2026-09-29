using PanelForge.Domain.Entities.Content;
using PanelForge.Application.Features.ConsistencyRules.Models;

namespace PanelForge.Application.Features.ConsistencyRules.Common;

public static class ConsistencyRuleExtensions
{
    public static ConsistencyRuleDto ToDto(this ConsistencyRule r) => new(
        Id: r.Id,
        SeriesId: r.SeriesId,
        Name: r.Name,
        RuleType: r.RuleType,
        Description: r.Description,
        IsEnabled: r.IsEnabled,
        Pattern: r.Pattern,
        Severity: r.Severity,
        ConfigurationJson: r.ConfigurationJson,
        CreatedAt: r.CreatedAt,
        UpdatedAt: r.UpdatedAt);
}
