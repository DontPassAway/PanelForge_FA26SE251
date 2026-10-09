using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Auth.Models;

public sealed record WorkspaceSummaryDto(
    Guid WorkspaceId,
    string Name,
    string Role);

public sealed record LandingContextDto(
    Guid UserId,
    string SystemRole,
    bool CanCreateStudio,
    IReadOnlyCollection<WorkspaceSummaryDto> Workspaces,
    string SuggestedLanding);
