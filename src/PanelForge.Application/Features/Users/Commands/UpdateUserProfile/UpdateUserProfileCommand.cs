using MediatR;
using PanelForge.Application.Features.Users.Models;

namespace PanelForge.Application.Features.Users.Commands.UpdateUserProfile;

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
