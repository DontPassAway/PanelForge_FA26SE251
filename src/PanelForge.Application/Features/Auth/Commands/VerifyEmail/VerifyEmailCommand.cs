using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Models;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Auth.Commands.VerifyEmail;

/// <summary>POST /api/auth/verify-email — xác thực email bằng OTP, thành công thì đăng nhập luôn.</summary>
public sealed record VerifyEmailCommand(string Email, string Token) : IRequest<Result<AuthResponse>>;

public sealed class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand, Result<AuthResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public VerifyEmailCommandHandler(IPanelForgeDbContext dbContext, IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<Result<AuthResponse>> Handle(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        var email = AuthSecrets.NormalizeEmail(command.Email);
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<AuthResponse>.Failure("Tài khoản không tồn tại hoặc đã bị vô hiệu hóa.");

        if (user.IsEmailConfirmed)
            return Result<AuthResponse>.Success(AuthResponseFactory.SignedIn(
                user, _jwtTokenGenerator, "Tài khoản đã được xác thực từ trước."));

        if (!AuthSecrets.OtpMatches(user.EmailVerificationToken, command.Token))
            return Result<AuthResponse>.Failure("Mã xác thực email không chính xác.");

        if (AuthSecrets.IsExpired(user.EmailVerificationTokenExpiresAt))
            return Result<AuthResponse>.Failure("Mã xác thực email đã hết hạn. Vui lòng yêu cầu gửi lại mã mới.");

        user.ConfirmEmail();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AuthResponse>.Success(AuthResponseFactory.SignedIn(
            user, _jwtTokenGenerator, "Xác thực tài khoản thành công!"));
    }
}
