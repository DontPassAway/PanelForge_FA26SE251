using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Admin.Queries.GetEmailSettings;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Platform;

namespace PanelForge.Application.Features.Admin.Commands.UpdateEmailSettings;

/// <summary>
/// PUT /api/admin/email-settings — Admin lưu cấu hình SMTP gửi email hệ thống. Append-only: thêm bản ghi mới và ghi audit
/// (BR-18). Mật khẩu mã hoá trước khi lưu; để trống thì giữ mật khẩu của bản ghi trước.
/// </summary>
public sealed record UpdateEmailSettingsCommand(
    Guid AdminUserId,
    string SmtpServer,
    int SmtpPort,
    string SenderName,
    string SenderEmail,
    string? Password
) : IRequest<Result<AdminEmailSettingsDto>>;

public sealed class UpdateEmailSettingsCommandHandler : IRequestHandler<UpdateEmailSettingsCommand, Result<AdminEmailSettingsDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAiKeyProtector _protector;

    public UpdateEmailSettingsCommandHandler(IPanelForgeDbContext db, IAiKeyProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<Result<AdminEmailSettingsDto>> Handle(UpdateEmailSettingsCommand command, CancellationToken ct)
    {
        var previous = await _db.PlatformEmailSettings.AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.SmtpServer, s.SmtpPort, s.SenderEmail, s.ProtectedPassword })
            .FirstOrDefaultAsync(ct);

        string protectedPassword;
        if (!string.IsNullOrWhiteSpace(command.Password))
            protectedPassword = _protector.Protect(command.Password.Trim());
        else if (previous is not null)
            protectedPassword = previous.ProtectedPassword;
        else
            return Result<AdminEmailSettingsDto>.Failure("Nhập mật khẩu SMTP cho lần cấu hình đầu tiên.", ResultErrorCodes.Validation);

        PlatformEmailSettings settings;
        try
        {
            settings = PlatformEmailSettings.Create(
                command.SmtpServer, command.SmtpPort, command.SenderName, command.SenderEmail, protectedPassword, command.AdminUserId);
        }
        catch (ArgumentException ex)
        {
            return Result<AdminEmailSettingsDto>.Failure(ex.Message.Split(" (Parameter")[0], ResultErrorCodes.Validation);
        }

        var passwordNote = string.IsNullOrWhiteSpace(command.Password) ? "giữ mật khẩu cũ" : "đổi mật khẩu";
        _db.PlatformEmailSettings.Add(settings);
        _db.AuditLogs.Add(AuditLog.Create(
            action: "PLATFORM_EMAIL_SETTINGS_CHANGED",
            entityName: nameof(PlatformEmailSettings),
            entityId: settings.Id.ToString(),
            userId: command.AdminUserId,
            details: $"Đổi cấu hình SMTP: {Describe(previous?.SmtpServer, previous?.SmtpPort, previous?.SenderEmail)} → "
                     + $"{Describe(settings.SmtpServer, settings.SmtpPort, settings.SenderEmail)} ({passwordNote})"));
        await _db.SaveChangesAsync(ct);

        return Result<AdminEmailSettingsDto>.Success(await GetEmailSettingsQueryHandler.LoadAsync(_db, ct));
    }

    private static string Describe(string? server, int? port, string? sender) =>
        server is null ? "cấu hình appsettings" : $"{sender} qua {server}:{port}";
}
