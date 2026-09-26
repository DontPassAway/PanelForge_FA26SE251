namespace PanelForge.Application.DTOs.Auth;

public record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    string? AvatarUrl,
    bool IsActive,
    bool IsEmailConfirmed = false,
    bool TwoFactorEnabled = false,
    string Role = "User",
    // BR-22: chỉ true khi Administrator đã cấp quyền tạo Studio (luôn false với Admin)
    bool CanCreateStudio = false
);
