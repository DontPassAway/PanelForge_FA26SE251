using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Models;

public record WorkspaceMemberDto(
    Guid UserId,
    string FullName,
    string Email,
    string? PhoneNumber,
    string? AvatarUrl,
    WorkspaceRole Role,
    bool IsOwner,
    DateTime JoinedAt
);
