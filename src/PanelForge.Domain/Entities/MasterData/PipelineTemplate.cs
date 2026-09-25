using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.MasterData;

/// <summary>
/// PipelineTemplate — DB-backed configurable pipeline blueprint (Task 5, NFR-08).
/// Replaces CreateDefaultMangaPipeline() hardcoded factory.
/// Only one template should be IsDefault = true at any time (enforced at Application layer).
/// </summary>
public class PipelineTemplate : BaseEntity
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsDefault { get; private set; } = false;
    public bool IsActive { get; private set; } = true;

    public ICollection<PipelineTemplateStage> Stages { get; private set; } = [];

    private PipelineTemplate() { }

    public static PipelineTemplate Create(
        string code,
        string name,
        string? description = null,
        bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new PipelineTemplate
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsDefault = isDefault,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(string name, string? description, bool isDefault, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim();
        IsDefault = isDefault;
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetAsDefault() { IsDefault = true; UpdatedAt = DateTime.UtcNow; }
    public void UnsetDefault() { IsDefault = false; UpdatedAt = DateTime.UtcNow; }
    public void Deactivate()   { IsActive = false; IsDefault = false; UpdatedAt = DateTime.UtcNow; }
    public void Activate()     { IsActive = true;  UpdatedAt = DateTime.UtcNow; }
}
