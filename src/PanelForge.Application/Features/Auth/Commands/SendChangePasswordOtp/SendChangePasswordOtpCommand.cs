using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Auth.Commands.SendChangePasswordOtp;

/// <summary>POST /api/auth/send-change-password-otp — gửi OTP email trước khi đổi mật khẩu.</summary>
public sealed record SendChangePasswordOtpCommand(Guid UserId) : IRequest<Result<bool>>;

public sealed class SendChangePasswordOtpCommandHandler : IRequestHandler<SendChangePasswordOtpCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IEmailService _emailService;

    public SendChangePasswordOtpCommandHandler(IPanelForgeDbContext dbContext, IEmailService emailService)
    {
        _dbContext = dbContext;
        _emailService = emailService;
    }

    public async Task<Result<bool>> Handle(SendChangePasswordOtpCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<bool>.Failure(
                "Người dùng không tồn tại hoặc đã bị vô hiệu hóa.", ResultErrorCodes.Unauthorized);

        var otp = AuthSecrets.NewEmailOtp();
        user.SetPasswordResetToken(otp, DateTime.UtcNow.Add(AuthSecrets.OtpLifetime));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _emailService.SendChangePasswordOtpAsync(user.Email, otp, user.FullName, cancellationToken);
        return Result<bool>.Success(true);
    }
}
