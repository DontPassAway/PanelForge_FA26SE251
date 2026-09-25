namespace PanelForge.Application.Features.ConsistencyRules.Commands;

/// <summary>HTTP request model for creating a ConsistencyRule.</summary>
public sealed record CreateConsistencyRuleRequest(
    string Name,
    string RuleType,
    string? Description = null,
    bool IsEnabled = true,
    string? Pattern = null,
    string Severity = "Warning",
    string? ConfigurationJson = null);

/// <summary>HTTP request model for updating a ConsistencyRule.</summary>
public sealed record UpdateConsistencyRuleRequest(
    string Name,
    string RuleType,
    string? Description = null,
    bool IsEnabled = true,
    string? Pattern = null,
    string Severity = "Warning",
    string? ConfigurationJson = null);
