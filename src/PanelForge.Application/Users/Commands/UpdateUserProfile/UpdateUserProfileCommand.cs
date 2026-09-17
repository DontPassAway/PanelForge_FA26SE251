using MediatR;
using PanelForge.Application.DTOs.Users;

namespace PanelForge.Application.Users.Commands.UpdateUserProfile;

public record UpdateUserProfileCommand(
    Guid? TargetUserId,
    string? TargetFirebaseUid,
    string CurrentUserFirebaseUid,
    string? CurrentUserIdStr,
    string Username,
    string? Avatar,
    string? PhoneNumber = null,
    bool IsAdmin = false
) : IRequest<UserProfileDto?>;
