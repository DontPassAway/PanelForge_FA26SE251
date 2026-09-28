using PanelForge.Domain.Entities.MasterData;
using PanelForge.Application.Features.MasterData.ElementTypes.Models;

namespace PanelForge.Application.Features.MasterData.ElementTypes.Common;

public static class ElementTypeMasterDataExtensions
{
    public static ElementTypeDto ToDto(this ElementTypeMasterData e) => new(
        e.Id, e.Code, e.Name, e.Description, e.AllowedPropertiesJson, e.IsActive, e.CreatedAt, e.UpdatedAt);
}
