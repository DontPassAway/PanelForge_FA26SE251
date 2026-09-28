
namespace PanelForge.Application.Features.MasterData.ElementTypes.Models;

public sealed record ElementTypeDto(
    Guid Id, string Code, string Name, string? Description,
    string AllowedPropertiesJson, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);
