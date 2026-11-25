using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.SeriesMembers.Models;

public record SeriesMemberDto(
    Guid Id,
    Guid SeriesId,
    Guid UserId,
    string FullName,
    string Email,
    string? AvatarUrl,
    WorkspaceRole Role,
    DateTime AssignedAt
);
