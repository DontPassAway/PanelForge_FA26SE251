using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Auth.Commands.ForgotPassword;

/// <summary>
/// POST /api/auth/forgot-password. Luôn thành công (kể cả khi email không tồn tại)
/// để tránh lộ danh sách email (email enumeration).
/// </summary>
public sealed record ForgotPasswordCommand(string Email) : IRequest<Result<bool>>;

public sealed class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IEmailService _emailService;

    public ForgotPasswordCommandHandler(IPanelForgeDbContext dbContext, IEmailService emailService)
    {
        _dbContext = dbContext;
        _emailService = emailService;
    }

    public async Task<Result<bool>> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var email = AuthSecrets.NormalizeEmail(command.Email);
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<bool>.Success(true);

        var otp = AuthSecrets.NewEmailOtp();
        user.SetPasswordResetToken(otp, DateTime.UtcNow.Add(AuthSecrets.OtpLifetime));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _emailService.SendPasswordResetEmailAsync(user.Email, otp, user.FullName, cancellationToken);
        return Result<bool>.Success(true);
    }
}
