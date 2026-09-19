using PanelForge.Application.DTOs.Workspaces;

namespace PanelForge.Application.Interfaces;

public interface IWorkspaceService
{
    Task<WorkspaceDto> CreateWorkspaceAsync(Guid ownerId, CreateWorkspaceRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<WorkspaceDto>> GetUserWorkspacesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<WorkspaceDto> GetWorkspaceByIdAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceDto> UpdateWorkspaceAsync(Guid userId, Guid workspaceId, UpdateWorkspaceRequest request, CancellationToken cancellationToken = default);
    Task DeleteWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);

    Task<IEnumerable<WorkspaceMemberDto>> GetMembersAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceMemberDto> AddMemberAsync(Guid actorId, Guid workspaceId, AddWorkspaceMemberRequest request, CancellationToken cancellationToken = default);
    Task<WorkspaceMemberDto> UpdateMemberRoleAsync(Guid actorId, Guid workspaceId, Guid targetUserId, UpdateMemberRoleRequest request, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(Guid actorId, Guid workspaceId, Guid targetUserId, CancellationToken cancellationToken = default);
}
