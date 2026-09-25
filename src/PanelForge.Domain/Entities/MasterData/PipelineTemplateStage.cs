using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.MasterData;

/// <summary>
/// PipelineTemplateStage — one stage in a PipelineTemplate blueprint.
/// Order must be unique within a template.
/// </summary>
public class PipelineTemplateStage : BaseEntity
{
    public Guid PipelineTemplateId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public int Order { get; private set; }
    public WorkspaceRole? RequiredRole { get; private set; }

    /// <summary>
    /// GateType: "Approval", "Review", or null for standard progress stage.
    /// </summary>
    public string? GateType { get; private set; }
    public bool IsRequired { get; private set; } = true;

    /// <summary>JSONB: stage-specific configuration (color, estimated days, etc.).</summary>
    public string ConfigurationJson { get; private set; } = "{}";

    public PipelineTemplate Template { get; private set; } = default!;

    private PipelineTemplateStage() { }

    public static PipelineTemplateStage Create(
        Guid templateId,
        string code,
        string name,
        int order,
        WorkspaceRole? requiredRole = null,
        string? gateType = null,
        bool isRequired = true,
        string? configurationJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (order < 1) throw new ArgumentOutOfRangeException(nameof(order), "Order must be >= 1.");

        return new PipelineTemplateStage
        {
            PipelineTemplateId = templateId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Order = order,
            RequiredRole = requiredRole,
            GateType = gateType?.Trim(),
            IsRequired = isRequired,
            ConfigurationJson = string.IsNullOrWhiteSpace(configurationJson)
                ? "{}" : configurationJson.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(
        string name, int order, WorkspaceRole? requiredRole,
        string? gateType, bool isRequired, string? configurationJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (order < 1) throw new ArgumentOutOfRangeException(nameof(order), "Order must be >= 1.");
        Name = name.Trim();
        Order = order;
        RequiredRole = requiredRole;
        GateType = gateType?.Trim();
        IsRequired = isRequired;
        if (!string.IsNullOrWhiteSpace(configurationJson))
            ConfigurationJson = configurationJson.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
