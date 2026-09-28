
namespace PanelForge.Application.Features.MasterData.ExportPresets.Models;

public sealed record ExportPresetDto(
    Guid Id, string Code, string Name, string FormatName,
    string ConfigOptionsJson, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);
