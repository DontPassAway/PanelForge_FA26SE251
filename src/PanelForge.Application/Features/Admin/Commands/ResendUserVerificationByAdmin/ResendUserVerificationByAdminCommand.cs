using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Auth.Common;
using PanelForge.Domain.Entities.Audit;

namespace PanelForge.Application.Features.Admin.Commands.ResendUserVerificationByAdmin;

/// <summary>POST /api/admin/users/{id}/resend-verification — Admin gửi lại mã OTP xác thực email cho người dùng.</summary>
public sealed record ResendUserVerificationByAdminCommand(Guid AdminUserId, Guid UserId)
    : IRequest<Result<AdminUserStatusDto>>;

public sealed class ResendUserVerificationByAdminCommandHandler
    : IRequestHandler<ResendUserVerificationByAdminCommand, Result<AdminUserStatusDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IEmailService _emailService;

    public ResendUserVerificationByAdminCommandHandler(IPanelForgeDbContext db, IEmailService emailService)
    {
        _db = db;
        _emailService = emailService;
    }

    public async Task<Result<AdminUserStatusDto>> Handle(ResendUserVerificationByAdminCommand command, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null)
            return Result<AdminUserStatusDto>.Failure("Không tìm thấy người dùng.", ResultErrorCodes.NotFound);
        if (user.IsEmailConfirmed)
            return Result<AdminUserStatusDto>.Failure("Tài khoản này đã xác thực email.", ResultErrorCodes.Conflict);
        // Tài khoản được mời (chưa có mật khẩu) xác thực qua lời mời Studio, không qua OTP đăng ký.
        if (user.IsPendingInvitation)
            return Result<AdminUserStatusDto>.Failure(
                "Tài khoản đang chờ nhận lời mời Studio. Hãy gửi lại lời mời thay vì mã xác thực.", ResultErrorCodes.Conflict);
        if (!user.IsActive)
            return Result<AdminUserStatusDto>.Failure("Tài khoản đang bị khóa. Mở khóa trước khi gửi mã xác thực.");

        var otp = AuthSecrets.NewEmailOtp();
        user.SetEmailVerificationToken(otp, DateTime.UtcNow.Add(AuthSecrets.OtpLifetime));

        var sent = await _emailService.SendEmailVerificationAsync(user.Email, otp, user.FullName, AuthSecrets.OtpLifetime, ct);

        _db.AuditLogs.Add(AuditLog.Create(
            action: sent ? "ADMIN_RESEND_EMAIL_VERIFICATION" : "ADMIN_RESEND_EMAIL_VERIFICATION_FAILED",
            entityName: "User",
            entityId: user.Id.ToString(),
            userId: command.AdminUserId,
            details: sent
                ? $"Admin gửi lại mã xác thực email tới {user.Email}."
                : $"Admin gửi lại mã xác thực email tới {user.Email} nhưng SMTP không gửi được."));
        await _db.SaveChangesAsync(ct);

        return sent
            ? Result<AdminUserStatusDto>.Success(new AdminUserStatusDto(user.Id, user.Email, user.IsActive))
            : Result<AdminUserStatusDto>.Failure(AuthSecrets.EmailDeliveryFailedMessage, ResultErrorCodes.EmailDeliveryFailed);
    }
}
