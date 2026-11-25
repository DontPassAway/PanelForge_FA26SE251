using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.SeriesMembers.Models;

public record UpdateSeriesMemberRoleRequest(
    WorkspaceRole Role
);
