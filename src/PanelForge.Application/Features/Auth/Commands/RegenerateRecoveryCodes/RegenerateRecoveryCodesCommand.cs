using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Application.Features.Auth.Models;

namespace PanelForge.Application.Features.Auth.Commands.RegenerateRecoveryCodes;

/// <summary>
/// POST /api/Auth/2fa/recovery-codes — tạo bộ mã khôi phục 2FA mới, mọi mã cũ hết hiệu lực.
/// Phải chứng minh yếu tố thứ hai (mã 6 số hoặc một mã khôi phục còn hiệu lực); mã thô chỉ trả lần này.
/// </summary>
public sealed record RegenerateRecoveryCodesCommand(Guid UserId, string Code) : IRequest<Result<RecoveryCodesResponse>>;

public sealed class RegenerateRecoveryCodesCommandHandler
    : IRequestHandler<RegenerateRecoveryCodesCommand, Result<RecoveryCodesResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly ITotpService _totpService;

    public RegenerateRecoveryCodesCommandHandler(IPanelForgeDbContext dbContext, ITotpService totpService)
    {
        _dbContext = dbContext;
        _totpService = totpService;
    }

    public async Task<Result<RecoveryCodesResponse>> Handle(RegenerateRecoveryCodesCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<RecoveryCodesResponse>.Failure(
                "Người dùng không tồn tại hoặc đã bị vô hiệu hóa.", ResultErrorCodes.Unauthorized);

        if (!user.TwoFactorEnabled)
            return Result<RecoveryCodesResponse>.Failure("Tài khoản chưa bật 2FA.");

        if (SecondFactor.Verify(user, _totpService, command.Code)
            is SecondFactor.Outcome.Invalid or SecondFactor.Outcome.AlreadyUsed)
            return Result<RecoveryCodesResponse>.Failure(
                "Mã xác thực 2FA hoặc mã khôi phục không chính xác, đã hết hạn hoặc đã được dùng.",
                ResultErrorCodes.Unauthorized);

        var codes = SecondFactor.IssueRecoveryCodes(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryCodesResponse>.Success(new RecoveryCodesResponse(codes));
    }
}
