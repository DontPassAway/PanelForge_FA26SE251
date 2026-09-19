using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.DTOs.Users;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Application.Users.Commands.UpdateUserProfile;

public class UpdateUserProfileCommandHandler : IRequestHandler<UpdateUserProfileCommand, UserProfileDto?>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateUserProfileCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserProfileDto?> Handle(UpdateUserProfileCommand request, CancellationToken cancellationToken)
    {
        User? user = null;

        if (request.TargetUserId.HasValue)
        {
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == request.TargetUserId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.TargetFirebaseUid))
        {
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUid == request.TargetFirebaseUid.Trim(), cancellationToken);
        }

        if (user is null)
        {
            return null; // Not found -> Controller returns 404
        }

        // Kiểm tra quyền sở hữu hồ sơ (Ownership Verification)
        bool isOwner = false;

        // Kiểm tra qua FirebaseUid từ JWT token
        if (!string.IsNullOrEmpty(user.FirebaseUid) && !string.IsNullOrWhiteSpace(request.CurrentUserFirebaseUid))
        {
            isOwner = string.Equals(user.FirebaseUid, request.CurrentUserFirebaseUid.Trim(), StringComparison.Ordinal);
        }

        // Hoặc kiểm tra qua PostgreSQL Id (nếu đăng nhập bằng JWT nội bộ)
        if (!isOwner && !string.IsNullOrWhiteSpace(request.CurrentUserIdStr) && Guid.TryParse(request.CurrentUserIdStr, out var currentGuid))
        {
            isOwner = user.Id == currentGuid;
        }

        // Cho phép System Admin thực hiện nếu cần
        if (request.IsAdmin)
        {
            isOwner = true;
        }

        if (!isOwner)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền chỉnh sửa hồ sơ của người dùng này.");
        }

        // Cập nhật Username (FullName) và Avatar (AvatarUrl)
        user.UpdateProfile(
            fullName: request.Username,
            phoneNumber: request.PhoneNumber ?? user.PhoneNumber,
            avatarUrl: request.Avatar
        );

        await _dbContext.SaveChangesAsync(cancellationToken);

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
