namespace PanelForge.Application.Features.Admin.Models;

public record AdminWorkspaceDto(
    Guid Id,
    string Name,
    Guid OwnerId,
    string OwnerName,
    string OwnerEmail,
    long StorageQuotaBytes,
    long UsedStorageBytes,
    int MemberCount,
    int SeriesCount,
    DateTime CreatedAt
);

public record UpdateWorkspaceQuotaRequest(
    long StorageQuotaBytes
);
