using MediatR;
using PanelForge.Application.Features.Users.Models;

namespace PanelForge.Application.Features.Users.Queries.GetUserProfile;

public record GetUserProfileQuery(
    Guid? Id = null,
    string? FirebaseUid = null
) : IRequest<UserProfileDto?>;
