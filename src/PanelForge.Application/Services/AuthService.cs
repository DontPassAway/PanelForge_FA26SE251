using Microsoft.EntityFrameworkCore;
using PanelForge.Application.DTOs.Auth;
using PanelForge.Application.Interfaces.Authentication;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Application.Services;

public class AuthService : IAuthService
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthService(
        IPanelForgeDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _dbContext.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (existingUser)
        {
            throw new InvalidOperationException("Email is already registered.");
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);

        var user = User.Create(
            email: normalizedEmail,
            fullName: request.FullName,
            passwordHash: passwordHash,
            phoneNumber: request.PhoneNumber
        );

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var (token, expiresAt) = _jwtTokenGenerator.GenerateToken(user);

        return new AuthResponse(
            Token: token,
            ExpiresAt: expiresAt,
            User: new UserDto(
                user.Id,
                user.Email,
                user.FullName,
                user.PhoneNumber,
                user.AvatarUrl,
                user.IsActive
            )
        );
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        var isValidPassword = _passwordHasher.VerifyPassword(request.Password, user.PasswordHash);
        if (!isValidPassword)
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        var (token, expiresAt) = _jwtTokenGenerator.GenerateToken(user);

        return new AuthResponse(
            Token: token,
            ExpiresAt: expiresAt,
            User: new UserDto(
                user.Id,
                user.Email,
                user.FullName,
                user.PhoneNumber,
                user.AvatarUrl,
                user.IsActive
            )
        );
    }
}
