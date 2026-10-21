using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Users.Models;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Application.Features.Users.Commands.SetMyProfileCover;

/// <summary>
/// Đặt bìa hồ sơ của chính người đang đăng nhập (chỉ tra theo danh tính trong JWT, không nhận userId từ client).
/// PresetId null = về bìa mặc định. ID không có trong danh mục → ArgumentException (400).
/// </summary>
public record SetMyProfileCoverCommand(Guid? CurrentUserId, string? CurrentFirebaseUid, string? PresetId)
    : IRequest<UserProfileDto?>;

public class SetMyProfileCoverCommandHandler : IRequestHandler<SetMyProfileCoverCommand, UserProfileDto?>
{
    private readonly IPanelForgeDbContext _dbContext;

    public SetMyProfileCoverCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<UserProfileDto?> Handle(SetMyProfileCoverCommand request, CancellationToken cancellationToken)
    {
        User? user = null;
        if (request.CurrentUserId.HasValue)
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == request.CurrentUserId.Value, cancellationToken);
        if (user is null && !string.IsNullOrWhiteSpace(request.CurrentFirebaseUid))
        {
            var uid = request.CurrentFirebaseUid.Trim();
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseUid == uid, cancellationToken);
        }
        if (user is null) return null;

        user.SetProfileCoverPreset(request.PresetId);
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
            CreatedAt: user.CreatedAt,
            ProfileCoverPresetId: user.ProfileCoverPresetId
        );
    }
}
