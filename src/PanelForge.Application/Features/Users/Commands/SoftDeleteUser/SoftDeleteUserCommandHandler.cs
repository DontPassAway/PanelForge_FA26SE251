using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Application.Users.Commands.SoftDeleteUser;

public class SoftDeleteUserCommandHandler : IRequestHandler<SoftDeleteUserCommand, bool?>
{
    private readonly IPanelForgeDbContext _dbContext;

    public SoftDeleteUserCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool?> Handle(SoftDeleteUserCommand request, CancellationToken cancellationToken)
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

        // Kiểm tra quyền sở hữu hoặc quyền Admin
        bool isAuthorized = false;

        if (!string.IsNullOrEmpty(user.FirebaseUid) && !string.IsNullOrWhiteSpace(request.CurrentUserFirebaseUid))
        {
            isAuthorized = string.Equals(user.FirebaseUid, request.CurrentUserFirebaseUid.Trim(), StringComparison.Ordinal);
        }

        if (!isAuthorized && !string.IsNullOrWhiteSpace(request.CurrentUserIdStr) && Guid.TryParse(request.CurrentUserIdStr, out var currentGuid))
        {
            isAuthorized = user.Id == currentGuid;
        }

        if (request.IsAdmin)
        {
            isAuthorized = true;
        }

        if (!isAuthorized)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền vô hiệu hóa (xóa) tài khoản này.");
        }

        // Thực hiện Soft Delete: chỉ chuyển IsActive = false, tuyệt đối KHÔNG xóa cứng khỏi database
        user.Deactivate();

        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
