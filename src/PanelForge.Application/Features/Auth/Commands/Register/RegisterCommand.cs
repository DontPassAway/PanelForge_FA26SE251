using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Models;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Application.Features.Auth.Commands.Register;

/// <summary>POST /api/auth/register — tạo tài khoản và gửi OTP xác thực email (UC-01).</summary>
public sealed record RegisterCommand(string Email, string Password, string FullName, string? PhoneNumber)
    : IRequest<Result<AuthResponse>>;

public sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, Result<AuthResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailService _emailService;

    public RegisterCommandHandler(IPanelForgeDbContext dbContext, IPasswordHasher passwordHasher, IEmailService emailService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
    }

    public async Task<Result<AuthResponse>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var email = AuthSecrets.NormalizeEmail(command.Email);

        if (await _dbContext.Users.AnyAsync(u => u.Email == email, cancellationToken))
            return Result<AuthResponse>.Failure("Email đã được đăng ký trong hệ thống.");

        var user = User.Create(
            email: email,
            fullName: command.FullName,
            passwordHash: _passwordHasher.HashPassword(command.Password),
            phoneNumber: command.PhoneNumber);

        var otp = AuthSecrets.NewEmailOtp();
        user.SetEmailVerificationToken(otp, DateTime.UtcNow.Add(AuthSecrets.OtpLifetime));

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _emailService.SendEmailVerificationAsync(user.Email, otp, user.FullName, cancellationToken);

        return Result<AuthResponse>.Success(AuthResponseFactory.WithoutToken(
            user, "Đăng ký tài khoản thành công. Vui lòng kiểm tra email để lấy mã PIN xác thực."));
    }
}
