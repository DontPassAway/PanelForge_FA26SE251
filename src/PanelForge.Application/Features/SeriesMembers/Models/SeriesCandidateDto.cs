namespace PanelForge.Application.Features.SeriesMembers.Models;

public record SeriesCandidateDto(
    Guid UserId,
    string FullName,
    string Email,
    string? AvatarUrl,
    string WorkspaceRole,
    bool IsAlreadyMember,
    string? CurrentSeriesRole
);
