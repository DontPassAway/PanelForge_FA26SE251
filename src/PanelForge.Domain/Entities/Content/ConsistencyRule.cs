using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// E-33: Series-level ConsistencyRule entity (Task 9).
/// Replaces ConsistencyRulesJson blob in SeriesPreset.
/// </summary>
public class ConsistencyRule : BaseEntity
{
    public Guid SeriesId { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary>
    /// RuleType: "CharacterContinuity" | "PropContinuity" | "LoreContinuity" |
    ///            "ToneConsistency" | "StrictSpelling" | "DialogueOverflow" | "Custom"
    /// </summary>
    public string RuleType { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsEnabled { get; private set; } = true;

    /// <summary>Optional regex or pattern string for rule evaluation.</summary>
    public string? Pattern { get; private set; }

    /// <summary>Severity: "Info" | "Warning" | "Error"</summary>
    public string Severity { get; private set; } = "Warning";

    /// <summary>JSONB: dynamic rule-specific configuration.</summary>
    public string ConfigurationJson { get; private set; } = "{}";

    public Series Series { get; private set; } = default!;

    private ConsistencyRule() { }

    public static ConsistencyRule Create(
        Guid seriesId,
        string name,
        string ruleType,
        string? description = null,
        bool isEnabled = true,
        string? pattern = null,
        string severity = "Warning",
        string? configurationJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleType);

        return new ConsistencyRule
        {
            SeriesId = seriesId,
            Name = name.Trim(),
            RuleType = ruleType.Trim(),
            Description = description?.Trim(),
            IsEnabled = isEnabled,
            Pattern = pattern?.Trim(),
            Severity = severity.Trim(),
            ConfigurationJson = string.IsNullOrWhiteSpace(configurationJson)
                ? "{}" : configurationJson.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(
        string name, string ruleType, string? description,
        bool isEnabled, string? pattern, string severity, string? configurationJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleType);
        Name = name.Trim();
        RuleType = ruleType.Trim();
        Description = description?.Trim();
        IsEnabled = isEnabled;
        Pattern = pattern?.Trim();
        Severity = severity.Trim();
        if (!string.IsNullOrWhiteSpace(configurationJson))
            ConfigurationJson = configurationJson.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void Enable()  { IsEnabled = true;  UpdatedAt = DateTime.UtcNow; }
    public void Disable() { IsEnabled = false; UpdatedAt = DateTime.UtcNow; }
}
