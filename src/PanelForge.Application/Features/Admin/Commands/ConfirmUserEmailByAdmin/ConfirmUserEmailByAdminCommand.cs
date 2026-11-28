using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Domain.Entities.Audit;

namespace PanelForge.Application.Features.Admin.Commands.ConfirmUserEmailByAdmin;

/// <summary>POST /api/admin/users/{id}/confirm-email — Admin đánh dấu email đã xác thực (bắt buộc ghi lý do).</summary>
public sealed record ConfirmUserEmailByAdminCommand(Guid AdminUserId, Guid UserId, string Reason)
    : IRequest<Result<AdminUserStatusDto>>;

public sealed class ConfirmUserEmailByAdminCommandHandler
    : IRequestHandler<ConfirmUserEmailByAdminCommand, Result<AdminUserStatusDto>>
{
    public const int MaxReasonLength = 500;
    private readonly IPanelForgeDbContext _db;

    public ConfirmUserEmailByAdminCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AdminUserStatusDto>> Handle(ConfirmUserEmailByAdminCommand command, CancellationToken ct)
    {
        var reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
            return Result<AdminUserStatusDto>.Failure("Vui lòng nhập lý do xác thực thủ công.");
        if (reason.Length > MaxReasonLength)
            return Result<AdminUserStatusDto>.Failure($"Lý do tối đa {MaxReasonLength} ký tự.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null)
            return Result<AdminUserStatusDto>.Failure("Không tìm thấy người dùng.", ResultErrorCodes.NotFound);
        if (user.IsEmailConfirmed)
            return Result<AdminUserStatusDto>.Failure("Tài khoản này đã xác thực email.", ResultErrorCodes.Conflict);
        // Xác thực tài khoản được mời mà chưa đặt mật khẩu sẽ làm mất trạng thái "chờ nhận lời mời" và khóa luôn tài khoản.
        if (user.IsPendingInvitation)
            return Result<AdminUserStatusDto>.Failure(
                "Tài khoản đang chờ nhận lời mời Studio và chưa có mật khẩu, không thể xác thực thủ công.", ResultErrorCodes.Conflict);

        user.ConfirmEmail();
        _db.AuditLogs.Add(AuditLog.Create(
            action: "ADMIN_CONFIRM_EMAIL",
            entityName: "User",
            entityId: user.Id.ToString(),
            userId: command.AdminUserId,
            details: $"Admin đánh dấu đã xác thực email {user.Email}. Lý do: {reason}"));
        await _db.SaveChangesAsync(ct);

        return Result<AdminUserStatusDto>.Success(new AdminUserStatusDto(user.Id, user.Email, user.IsActive));
    }
}
