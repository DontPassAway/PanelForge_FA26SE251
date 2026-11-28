using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Admin.Queries.GetEmailSettings;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Platform;

namespace PanelForge.Application.Features.Admin.Commands.RestoreEmailSettings;

/// <summary>
/// POST /api/admin/email-settings/history/{versionId}/restore — dùng lại một phiên bản cấu hình SMTP cũ.
/// Append-only: không sửa bản cũ mà thêm bản mới chép nguyên giá trị (kể cả mật khẩu đã mã hoá) và ghi audit (BR-18).
/// </summary>
public sealed record RestoreEmailSettingsCommand(Guid AdminUserId, Guid VersionId) : IRequest<Result<AdminEmailSettingsDto>>;

public sealed class RestoreEmailSettingsCommandHandler : IRequestHandler<RestoreEmailSettingsCommand, Result<AdminEmailSettingsDto>>
{
    private readonly IPanelForgeDbContext _db;

    public RestoreEmailSettingsCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AdminEmailSettingsDto>> Handle(RestoreEmailSettingsCommand command, CancellationToken ct)
    {
        var version = await _db.PlatformEmailSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == command.VersionId && !s.IsDeleted, ct);
        if (version is null)
            return Result<AdminEmailSettingsDto>.Failure("Không tìm thấy phiên bản cấu hình email.", ResultErrorCodes.NotFound);

        var currentId = await _db.PlatformEmailSettings.AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Id)
            .FirstAsync(ct);
        if (currentId == version.Id)
            return Result<AdminEmailSettingsDto>.Failure("Phiên bản này đang được dùng.", ResultErrorCodes.Conflict);

        var restored = PlatformEmailSettings.Create(
            version.SmtpServer, version.SmtpPort, version.SenderName, version.SenderEmail, version.ProtectedPassword, command.AdminUserId);
        _db.PlatformEmailSettings.Add(restored);
        _db.AuditLogs.Add(AuditLog.Create(
            action: "PLATFORM_EMAIL_SETTINGS_RESTORED",
            entityName: nameof(PlatformEmailSettings),
            entityId: restored.Id.ToString(),
            userId: command.AdminUserId,
            details: $"Khôi phục cấu hình SMTP ngày {version.CreatedAt:yyyy-MM-dd HH:mm} UTC: "
                     + $"{version.SenderEmail} qua {version.SmtpServer}:{version.SmtpPort} (phiên bản {version.Id})"));
        await _db.SaveChangesAsync(ct);

        return Result<AdminEmailSettingsDto>.Success(await GetEmailSettingsQueryHandler.LoadAsync(_db, ct));
    }
}
