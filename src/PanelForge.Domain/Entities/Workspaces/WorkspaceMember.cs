using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Domain.Entities.Workspaces;

public class WorkspaceMember : BaseEntity
{
    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public WorkspaceRole Role { get; private set; }
    public WorkspaceMemberStatus Status { get; private set; } = WorkspaceMemberStatus.Active;
    public DateTime JoinedAt { get; private set; } = DateTime.UtcNow;

    public StudioWorkspace Workspace { get; private set; } = default!;
    public User User { get; private set; } = default!;

    private WorkspaceMember() { }

    public static WorkspaceMember Create(
        Guid workspaceId,
        Guid userId,
        WorkspaceRole role,
        WorkspaceMemberStatus status = WorkspaceMemberStatus.Active)
    {
        return new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = role,
            Status = status
        };
    }

    public void ChangeRole(WorkspaceRole newRole) => Role = newRole;

    public void Accept() => Status = WorkspaceMemberStatus.Active;

    public void Reject() => Status = WorkspaceMemberStatus.Rejected;
}
