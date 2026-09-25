namespace PanelForge.Application.Features.Audit.Models;

public sealed record AuditLogDto(
    Guid Id,
    Guid? WorkspaceId,
    Guid? UserId,
    string? UserEmail,
    string Action,
    string EntityName,
    string? EntityId,
    string? ChangesJson,
    DateTime TimestampUtc,
    string? IpAddress,
    string? Details
);

public sealed record AuditLogsPagedResponse(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<AuditLogDto> Items
);
