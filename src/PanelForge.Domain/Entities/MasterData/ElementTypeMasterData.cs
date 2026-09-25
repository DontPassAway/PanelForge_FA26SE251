using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.MasterData;

/// <summary>
/// E-30: ElementType Master Data — configurable via Admin UI (NFR-08).
/// Thay thế enum ElementType cứng bằng dữ liệu DB.
/// Ví dụ: SPEECH_BALLOON, CAPTION, SOUND_EFFECT, CHARACTER_INSTANCE, ARTWORK_LAYER
/// </summary>
public class ElementTypeMasterData : BaseEntity
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>JSONB: dynamic schema-like metadata for allowed properties.</summary>
    public string AllowedPropertiesJson { get; private set; } = "{}";

    public bool IsActive { get; private set; } = true;

    private ElementTypeMasterData() { }

    public static ElementTypeMasterData Create(
        string code,
        string name,
        string? description = null,
        string? allowedPropertiesJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new ElementTypeMasterData
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Description = description?.Trim(),
            AllowedPropertiesJson = string.IsNullOrWhiteSpace(allowedPropertiesJson)
                ? "{}" : allowedPropertiesJson.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(string name, string? description, string? allowedPropertiesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim();
        if (!string.IsNullOrWhiteSpace(allowedPropertiesJson))
            AllowedPropertiesJson = allowedPropertiesJson.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate() { IsActive = false; UpdatedAt = DateTime.UtcNow; }
    public void Activate()   { IsActive = true;  UpdatedAt = DateTime.UtcNow; }
}
