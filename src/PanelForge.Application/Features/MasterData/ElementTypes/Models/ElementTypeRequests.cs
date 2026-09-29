
namespace PanelForge.Application.Features.MasterData.ElementTypes.Models;

public sealed record CreateElementTypeRequest(string Code, string Name, string? Description, string? AllowedPropertiesJson);

public sealed record UpdateElementTypeRequest(string Name, string? Description, string? AllowedPropertiesJson, bool IsActive);
