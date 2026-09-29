using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Auth.Commands.ResendVerificationEmail;

/// <summary>POST /api/auth/resend-verification — gửi lại OTP xác thực email.</summary>
public sealed record ResendVerificationEmailCommand(string Email) : IRequest<Result<bool>>;

public sealed class ResendVerificationEmailCommandHandler : IRequestHandler<ResendVerificationEmailCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IEmailService _emailService;

    public ResendVerificationEmailCommandHandler(IPanelForgeDbContext dbContext, IEmailService emailService)
    {
        _dbContext = dbContext;
        _emailService = emailService;
    }

    public async Task<Result<bool>> Handle(ResendVerificationEmailCommand command, CancellationToken cancellationToken)
    {
        var email = AuthSecrets.NormalizeEmail(command.Email);
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Không lộ việc email có tồn tại hay không
        if (user is null || !user.IsActive)
            return Result<bool>.Success(true);

        if (user.IsEmailConfirmed)
            return Result<bool>.Failure("Tài khoản email này đã được xác thực trước đó.");

        var otp = AuthSecrets.NewEmailOtp();
        user.SetEmailVerificationToken(otp, DateTime.UtcNow.Add(AuthSecrets.OtpLifetime));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _emailService.SendEmailVerificationAsync(user.Email, otp, user.FullName, cancellationToken);
        return Result<bool>.Success(true);
    }
}
