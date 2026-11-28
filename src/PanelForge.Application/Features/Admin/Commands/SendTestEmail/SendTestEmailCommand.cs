using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Models;

namespace PanelForge.Application.Features.Admin.Commands.SendTestEmail;

/// <summary>POST /api/admin/email-settings/test — gửi một email thử tới chính Admin bằng cấu hình SMTP đang dùng.</summary>
public sealed record SendTestEmailCommand(Guid AdminUserId) : IRequest<Result<SendTestEmailResponse>>;

public sealed class SendTestEmailCommandHandler : IRequestHandler<SendTestEmailCommand, Result<SendTestEmailResponse>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IEmailService _email;

    public SendTestEmailCommandHandler(IPanelForgeDbContext db, IEmailService email)
    {
        _db = db;
        _email = email;
    }

    public async Task<Result<SendTestEmailResponse>> Handle(SendTestEmailCommand command, CancellationToken ct)
    {
        var admin = await _db.Users.AsNoTracking()
            .Where(u => u.Id == command.AdminUserId)
            .Select(u => new { u.Email })
            .FirstOrDefaultAsync(ct);
        if (admin is null)
            return Result<SendTestEmailResponse>.Failure("Không tìm thấy tài khoản Admin.", ResultErrorCodes.NotFound);

        const string body = "<p>Đây là email thử từ PanelForge. Cấu hình SMTP của hệ thống đang hoạt động.</p>";
        var sent = await _email.SendEmailAsync(admin.Email, "[PanelForge] Email thử cấu hình SMTP", body, ct);

        return sent
            ? Result<SendTestEmailResponse>.Success(new SendTestEmailResponse(admin.Email))
            : Result<SendTestEmailResponse>.Failure(
                "Không gửi được email thử. Kiểm tra máy chủ, cổng, email gửi đi và mật khẩu ứng dụng.",
                ResultErrorCodes.EmailDeliveryFailed);
    }
}
