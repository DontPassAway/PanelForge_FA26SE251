using PanelForge.Domain.Entities.MasterData;
using PanelForge.Application.Features.MasterData.ExportPresets.Models;

namespace PanelForge.Application.Features.MasterData.ExportPresets.Common;

public static class ExportPresetExtensions
{
    public static ExportPresetDto ToDto(this ExportPreset e) => new(
        e.Id, e.Code, e.Name, e.FormatName, e.ConfigOptionsJson, e.IsActive, e.CreatedAt, e.UpdatedAt);
}
