namespace PanelForge.Application.DTOs.Users;

public record UserProfileDto(
    Guid Id,
    string? FirebaseUid,
    string Username,
    string Email,
    string? Avatar,
    string? PhoneNumber,
    bool IsActive,
    string Role,
    DateTime CreatedAt
);
