using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Auth;

public class WorkspaceMember : BaseEntity
{
    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public WorkspaceRole Role { get; private set; }
    public DateTime JoinedAt { get; private set; } = DateTime.UtcNow;

    public StudioWorkspace Workspace { get; private set; } = default!;
    public User User { get; private set; } = default!;

    private WorkspaceMember() { }

    public static WorkspaceMember Create(Guid workspaceId, Guid userId, WorkspaceRole role)
    {
        return new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = role
        };
    }

    public void ChangeRole(WorkspaceRole newRole) => Role = newRole;
}
