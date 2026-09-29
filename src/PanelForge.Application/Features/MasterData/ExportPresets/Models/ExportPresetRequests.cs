
namespace PanelForge.Application.Features.MasterData.ExportPresets.Models;

public sealed record CreateExportPresetRequest(string Code, string Name, string FormatName, string? ConfigOptionsJson);

public sealed record UpdateExportPresetRequest(string Name, string FormatName, string? ConfigOptionsJson, bool IsActive);
