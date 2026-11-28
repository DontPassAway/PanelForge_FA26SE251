using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Admin.Models;

namespace PanelForge.Application.Features.Admin.Queries.GetEmailSettings;

/// <summary>GET /api/admin/email-settings — cấu hình SMTP hệ thống đang dùng (bản ghi mới nhất), không kèm mật khẩu.</summary>
public sealed record GetEmailSettingsQuery : IRequest<Result<AdminEmailSettingsDto>>;

public sealed class GetEmailSettingsQueryHandler : IRequestHandler<GetEmailSettingsQuery, Result<AdminEmailSettingsDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetEmailSettingsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AdminEmailSettingsDto>> Handle(GetEmailSettingsQuery query, CancellationToken ct)
        => Result<AdminEmailSettingsDto>.Success(await LoadAsync(_db, ct));

    internal static async Task<AdminEmailSettingsDto> LoadAsync(IPanelForgeDbContext db, CancellationToken ct)
    {
        var current = await db.PlatformEmailSettings.AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new AdminEmailSettingsDto(
                true,
                s.SmtpServer,
                s.SmtpPort,
                s.SenderName,
                s.SenderEmail,
                true,
                s.CreatedAt,
                db.Users.Where(u => u.Id == s.ChangedByUserId).Select(u => u.FullName).FirstOrDefault()))
            .FirstOrDefaultAsync(ct);

        return current ?? new AdminEmailSettingsDto(false, null, null, null, null, false, null, null);
    }
}
