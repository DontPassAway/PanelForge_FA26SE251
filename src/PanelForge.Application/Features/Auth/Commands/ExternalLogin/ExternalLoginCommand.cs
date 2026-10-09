using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Models;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Application.Features.Auth.Commands.ExternalLogin;

/// <summary>
/// POST /api/auth/external-login — đăng nhập bằng ID token của nhà cung cấp ngoài (Firebase).
/// Chưa có tài khoản → tạo mới; đã có theo email → liên kết.
/// </summary>
public sealed record ExternalLoginCommand(string IdToken) : IRequest<Result<AuthResponse>>;

public sealed class ExternalLoginCommandHandler : IRequestHandler<ExternalLoginCommand, Result<AuthResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IExternalIdentityVerifier _identityVerifier;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public ExternalLoginCommandHandler(
        IPanelForgeDbContext dbContext,
        IExternalIdentityVerifier identityVerifier,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _identityVerifier = identityVerifier;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<Result<AuthResponse>> Handle(ExternalLoginCommand command, CancellationToken cancellationToken)
    {
        var identity = await _identityVerifier.VerifyAsync(command.IdToken, cancellationToken);
        switch (identity.Status)
        {
            case ExternalIdentityStatus.NotConfigured:
                return Result<AuthResponse>.Failure(identity.ErrorMessage!);
            case ExternalIdentityStatus.InvalidToken:
                return Result<AuthResponse>.Failure(identity.ErrorMessage!, ResultErrorCodes.Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(identity.Email))
            return Result<AuthResponse>.Failure("Firebase Token không chứa thông tin email hợp lệ.");

        var uid = identity.Uid!;
        var email = AuthSecrets.NormalizeEmail(identity.Email);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.FirebaseUid == uid || u.Email == email, cancellationToken);

        if (user is null)
        {
            user = User.Create(
                email: email,
                fullName: !string.IsNullOrWhiteSpace(identity.Name) ? identity.Name : email.Split('@')[0],
                passwordHash: null,
                firebaseUid: uid,
                avatarUrl: identity.PictureUrl);
            user.ConfirmEmail();
            _dbContext.Users.Add(user);
        }
        else
        {
            if (!user.IsActive)
                return Result<AuthResponse>.Failure("Tài khoản này đã bị vô hiệu hóa.", ResultErrorCodes.Unauthorized);

            if (string.IsNullOrEmpty(user.FirebaseUid))
                user.LinkFirebase(uid);

            if (!user.IsEmailConfirmed)
                user.ConfirmEmail();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (user.TwoFactorEnabled)
            return Result<AuthResponse>.Success(AuthResponseFactory.TwoFactorChallenge(user));

        return Result<AuthResponse>.Success(await AuthResponseFactory.SignedInAsync(_dbContext, cancellationToken, 
            user, _jwtTokenGenerator, "Đăng nhập mạng xã hội thành công."));
    }
}
