using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.MasterData;

/// <summary>
/// E-32: ExportPreset — system-level configurable export format (NFR-08).
/// Examples: PDF, CBZ, PAGE_IMAGES, WEBTOON, OPEN_JSON
/// </summary>
public class ExportPreset : BaseEntity
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string FormatName { get; private set; } = default!;

    /// <summary>JSONB: flexible export configuration metadata.</summary>
    public string ConfigOptionsJson { get; private set; } = "{}";

    public bool IsActive { get; private set; } = true;

    private ExportPreset() { }

    public static ExportPreset Create(
        string code,
        string name,
        string formatName,
        string? configOptionsJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(formatName);

        return new ExportPreset
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            FormatName = formatName.Trim(),
            ConfigOptionsJson = string.IsNullOrWhiteSpace(configOptionsJson)
                ? "{}" : configOptionsJson.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(string name, string formatName, string? configOptionsJson, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(formatName);
        Name = name.Trim();
        FormatName = formatName.Trim();
        if (!string.IsNullOrWhiteSpace(configOptionsJson))
            ConfigOptionsJson = configOptionsJson.Trim();
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate() { IsActive = false; UpdatedAt = DateTime.UtcNow; }
    public void Activate()   { IsActive = true;  UpdatedAt = DateTime.UtcNow; }
}
