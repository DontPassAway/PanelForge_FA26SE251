using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Models;

public record WorkspaceDto(
    Guid Id,
    string Name,
    Guid OwnerId,
    string OwnerName,
    string OwnerEmail,
    long StorageQuotaBytes,
    long UsedStorageBytes,
    DateTime CreatedAt,
    bool IsOwner,
    WorkspaceRole? CurrentUserRole,
    int MemberCount,
    int PendingTasks = 0
);
