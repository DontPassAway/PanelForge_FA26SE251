using MediatR;
using PanelForge.Application.DTOs.Users;

namespace PanelForge.Application.Users.Queries.GetUserProfile;

public record GetUserProfileQuery(
    Guid? Id = null,
    string? FirebaseUid = null
) : IRequest<UserProfileDto?>;
