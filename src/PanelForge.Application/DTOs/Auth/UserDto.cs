namespace PanelForge.Application.DTOs.Auth;

public record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    string? AvatarUrl,
    bool IsActive
);
