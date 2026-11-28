using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Email;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Commands.UpdateEmailTemplate;
using PanelForge.Application.Features.Admin.Models;

namespace PanelForge.Application.Features.Admin.Commands.SendTestEmailTemplate;

/// <summary>
/// POST /api/admin/email-templates/{key}/test — gửi nội dung đang soạn (chưa cần lưu) tới chính Admin với dữ liệu mẫu,
/// để Admin xem email thật trong hộp thư trước khi lưu.
/// </summary>
public sealed record SendTestEmailTemplateCommand(Guid AdminUserId, string Key, EmailTemplateContent Content)
    : IRequest<Result<SendTestEmailResponse>>;

public sealed class SendTestEmailTemplateCommandHandler
    : IRequestHandler<SendTestEmailTemplateCommand, Result<SendTestEmailResponse>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IEmailService _email;

    public SendTestEmailTemplateCommandHandler(IPanelForgeDbContext db, IEmailService email)
    {
        _db = db;
        _email = email;
    }

    public async Task<Result<SendTestEmailResponse>> Handle(SendTestEmailTemplateCommand command, CancellationToken ct)
    {
        var draft = EmailTemplateDraft.Parse(command.AdminUserId, command.Key, command.Content);
        if (!draft.IsSuccess)
            return Result<SendTestEmailResponse>.Failure(draft.ErrorMessage!, draft.ErrorCode);

        var admin = await _db.Users.AsNoTracking()
            .Where(u => u.Id == command.AdminUserId)
            .Select(u => new { u.Email, u.FullName })
            .FirstOrDefaultAsync(ct);
        if (admin is null)
            return Result<SendTestEmailResponse>.Failure("Không tìm thấy tài khoản Admin.", ResultErrorCodes.NotFound);

        var (definition, template) = draft.Value!;
        var (subject, body) = EmailTemplateCatalog.Render(
            definition, EmailTemplateDraft.ContentOf(template), EmailTemplateCatalog.SampleValues(admin.FullName));

        return await _email.SendEmailAsync(admin.Email, "[Thử] " + subject, body, ct)
            ? Result<SendTestEmailResponse>.Success(new SendTestEmailResponse(admin.Email))
            : Result<SendTestEmailResponse>.Failure(
                "Không gửi được email thử. Kiểm tra Cấu hình email (máy chủ, cổng, mật khẩu).",
                ResultErrorCodes.EmailDeliveryFailed);
    }
}
