using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.SeriesMembers.Models;

public record AddSeriesMemberRequest(
    Guid UserId,
    WorkspaceRole Role
);
