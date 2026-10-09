namespace PanelForge.Application.Features.Admin.Models;

// Shape JSON giữ nguyên như response cũ của AdminController (FE đang dùng).

public sealed record AdminUserListItemDto(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    string Role,
    bool CanCreateStudio,
    bool IsActive,
    bool IsEmailConfirmed,
    bool TwoFactorEnabled,
    DateTime CreatedAt);

public sealed record AdminUserListResponse(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<AdminUserListItemDto> Items);

public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    string Role,
    bool IsActive,
    bool IsEmailConfirmed,
    DateTime CreatedAt);

public sealed record AssignUserRoleResponse(
    string Message,
    Guid UserId,
    string NewRole,
    bool CanCreateStudio);

public sealed record AdminUserStatusDto(
    Guid Id,
    string Email,
    bool IsActive);

public sealed record SystemStatsDto(
    int TotalUsers,
    int Admins,
    int RegisteredUsers,
    int TotalWorkspaces,
    int TotalWorkspaceMembers,
    DateTime ServerTimeUtc);
