using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.DTOs.Users;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Application.Users.Queries.GetUserProfile;

public class GetUserProfileQueryHandler : IRequestHandler<GetUserProfileQuery, UserProfileDto?>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetUserProfileQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserProfileDto?> Handle(GetUserProfileQuery request, CancellationToken cancellationToken)
    {
        if (!request.Id.HasValue && string.IsNullOrWhiteSpace(request.FirebaseUid))
        {
            throw new ArgumentException("Phải cung cấp ít nhất PostgreSQL Id hoặc FirebaseUid để truy vấn.");
        }

        IQueryable<User> query = _dbContext.Users.AsNoTracking();

        User? user = null;
        if (request.Id.HasValue)
        {
            user = await query.FirstOrDefaultAsync(u => u.Id == request.Id.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.FirebaseUid))
        {
            var trimmedUid = request.FirebaseUid.Trim();
            user = await query.FirstOrDefaultAsync(u => u.FirebaseUid == trimmedUid, cancellationToken);
        }

        if (user is null)
        {
            return null;
        }

        return new UserProfileDto(
            Id: user.Id,
            FirebaseUid: user.FirebaseUid,
            Username: user.FullName,
            Email: user.Email,
            Avatar: user.AvatarUrl,
            PhoneNumber: user.PhoneNumber,
            IsActive: user.IsActive,
            Role: user.Role.ToString(),
            CreatedAt: user.CreatedAt
        );
    }
}
