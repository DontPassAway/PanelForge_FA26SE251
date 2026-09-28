using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Common;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Admin.Commands.CreateUserByAdmin;

/// <summary>POST /api/admin/users — Administrator tạo tài khoản mới (UC-01).</summary>
public sealed record CreateUserByAdminCommand(
    string Email,
    string FullName,
    string Password,
    string? PhoneNumber,
    SystemRole Role,
    bool IsActive) : IRequest<Result<AdminUserDto>>;

public sealed class CreateUserByAdminCommandHandler
    : IRequestHandler<CreateUserByAdminCommand, Result<AdminUserDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;

    public CreateUserByAdminCommandHandler(IPanelForgeDbContext dbContext, IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<AdminUserDto>> Handle(
        CreateUserByAdminCommand command,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Role))
            return Result<AdminUserDto>.Failure("Vai trò hệ thống không hợp lệ. Hợp lệ: Admin, User.");

        var normalizedEmail = command.Email.Trim().ToLowerInvariant();
        var emailTaken = await _dbContext.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (emailTaken)
            return Result<AdminUserDto>.Failure($"Email '{command.Email}' đã tồn tại trong hệ thống.");

        var newUser = User.Create(
            email: normalizedEmail,
            fullName: command.FullName,
            passwordHash: _passwordHasher.HashPassword(command.Password),
            phoneNumber: command.PhoneNumber,
            role: command.Role);

        if (command.IsActive)
        {
            newUser.Activate();
            newUser.ConfirmEmail();
        }
        else
        {
            newUser.Deactivate();
        }

        _dbContext.Users.Add(newUser);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AdminUserDto>.Success(AdminUserMapper.ToDto(newUser));
    }
}
