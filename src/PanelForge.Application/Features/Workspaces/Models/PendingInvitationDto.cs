using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Models;

public record PendingInvitationDto(
    Guid WorkspaceId,
    string WorkspaceName,
    string OwnerName,
    string OwnerEmail,
    WorkspaceRole Role,
    DateTime InvitedAt
);
