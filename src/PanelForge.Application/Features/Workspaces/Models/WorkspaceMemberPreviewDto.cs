namespace PanelForge.Application.Features.Workspaces.Models;

public record WorkspaceMemberPreviewDto(
    Guid UserId,
    string FullName,
    string? AvatarUrl
);
