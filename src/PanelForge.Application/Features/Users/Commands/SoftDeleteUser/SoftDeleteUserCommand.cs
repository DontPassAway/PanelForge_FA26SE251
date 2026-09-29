using MediatR;

namespace PanelForge.Application.Features.Users.Commands.SoftDeleteUser;

public record SoftDeleteUserCommand(
    Guid? TargetUserId,
    string? TargetFirebaseUid,
    string CurrentUserFirebaseUid,
    string? CurrentUserIdStr,
    bool IsAdmin = false
) : IRequest<bool?>;
