using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Application.Features.Auth.Commands.ForgetDevice;

/// <summary>
/// POST /api/auth/forget-device — xoá thiết bị tin cậy theo token (cookie / header),
/// hoặc toàn bộ thiết bị của người dùng đang đăng nhập.
/// </summary>
public sealed record ForgetDeviceCommand(string? RememberDeviceToken, Guid? CurrentUserId) : IRequest<Result<bool>>;

public sealed class ForgetDeviceCommandHandler : IRequestHandler<ForgetDeviceCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public ForgetDeviceCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<bool>> Handle(ForgetDeviceCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RememberDeviceToken) && !command.CurrentUserId.HasValue)
            return Result<bool>.Success(true);

        IQueryable<UserRememberedDevice> query = _dbContext.UserRememberedDevices;

        if (!string.IsNullOrWhiteSpace(command.RememberDeviceToken))
        {
            var tokenHash = AuthSecrets.HashToken(command.RememberDeviceToken.Trim());
            query = query.Where(d => d.TokenHash == tokenHash);
        }

        if (command.CurrentUserId.HasValue)
            query = query.Where(d => d.UserId == command.CurrentUserId.Value);

        var devices = await query.ToListAsync(cancellationToken);
        if (devices.Count > 0)
        {
            foreach (var device in devices)
                _dbContext.UserRememberedDevices.Remove(device);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result<bool>.Success(true);
    }
}
