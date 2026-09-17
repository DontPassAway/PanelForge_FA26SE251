using PanelForge.Domain.Enums;

namespace PanelForge.Application.Interfaces;

public interface IWorkspaceAuthorizationService
{
    Task<bool> IsOwnerAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<bool> IsOwnerOrProducerAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<bool> HasWorkspaceRoleAsync(Guid userId, Guid workspaceId, WorkspaceRole[] allowedRoles, CancellationToken cancellationToken = default);
    Task<bool> IsMemberAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceRole?> GetMemberRoleAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
}
