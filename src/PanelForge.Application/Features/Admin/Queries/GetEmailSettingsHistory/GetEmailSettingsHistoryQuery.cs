using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Admin.Models;

namespace PanelForge.Application.Features.Admin.Queries.GetEmailSettingsHistory;

/// <summary>GET /api/admin/email-settings/history — các phiên bản cấu hình SMTP, mới nhất trước (tối đa 50).</summary>
public sealed record GetEmailSettingsHistoryQuery : IRequest<Result<IReadOnlyList<AdminEmailSettingsVersionDto>>>;

public sealed class GetEmailSettingsHistoryQueryHandler
    : IRequestHandler<GetEmailSettingsHistoryQuery, Result<IReadOnlyList<AdminEmailSettingsVersionDto>>>
{
    public const int MaxVersions = 50;
    private readonly IPanelForgeDbContext _db;

    public GetEmailSettingsHistoryQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<AdminEmailSettingsVersionDto>>> Handle(GetEmailSettingsHistoryQuery query, CancellationToken ct)
    {
        var rows = await _db.PlatformEmailSettings.AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .Take(MaxVersions)
            .Select(s => new
            {
                s.Id,
                s.SmtpServer,
                s.SmtpPort,
                s.SenderName,
                s.SenderEmail,
                s.CreatedAt,
                ChangedByName = _db.Users.Where(u => u.Id == s.ChangedByUserId).Select(u => u.FullName).FirstOrDefault()
            })
            .ToListAsync(ct);

        IReadOnlyList<AdminEmailSettingsVersionDto> versions = rows
            .Select((r, i) => new AdminEmailSettingsVersionDto(
                r.Id, r.SmtpServer, r.SmtpPort, r.SenderName, r.SenderEmail, r.CreatedAt, r.ChangedByName, i == 0))
            .ToList();
        return Result<IReadOnlyList<AdminEmailSettingsVersionDto>>.Success(versions);
    }
}
