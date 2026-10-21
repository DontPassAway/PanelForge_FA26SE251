using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Common;

internal static class WorkspaceMapper
{
    /// <summary>Owner luôn được coi là Producer của Studio mình tạo (BR-22).</summary>
    public static WorkspaceRole? RoleOf(StudioWorkspace workspace, Guid userId)
        => workspace.OwnerId == userId
            ? WorkspaceRole.Producer
            : workspace.Members.FirstOrDefault(m => m.UserId == userId && m.Status == WorkspaceMemberStatus.Active)?.Role;

    /// <summary>Yêu cầu workspace đã Include(Owner) và Include(Members).</summary>
    public static WorkspaceDto ToDto(StudioWorkspace workspace, Guid currentUserId, int memberCount, int pendingTasks = 0)
        => new(
            Id: workspace.Id,
            Name: workspace.Name,
            OwnerId: workspace.Owner.Id,
            OwnerName: workspace.Owner.FullName,
            OwnerEmail: workspace.Owner.Email,
            StorageQuotaBytes: workspace.StorageQuotaBytes,
            UsedStorageBytes: workspace.UsedStorageBytes,
            CreatedAt: workspace.CreatedAt,
            IsOwner: workspace.OwnerId == currentUserId,
            CurrentUserRole: RoleOf(workspace, currentUserId),
            MemberCount: memberCount,
            PendingTasks: pendingTasks);

    public static WorkspaceMemberDto ToMemberDto(WorkspaceMember member, User user, Guid ownerId)
        => new(
            UserId: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            PhoneNumber: user.PhoneNumber,
            AvatarUrl: user.AvatarUrl,
            Role: member.Role,
            IsOwner: user.Id == ownerId,
            JoinedAt: member.JoinedAt,
            IsEmailConfirmed: user.IsEmailConfirmed,
            Status: member.Status);
}
