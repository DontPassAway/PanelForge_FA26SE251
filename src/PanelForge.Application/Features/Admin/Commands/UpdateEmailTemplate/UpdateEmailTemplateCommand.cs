using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Email;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Admin.Queries.GetEmailTemplates;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Platform;

namespace PanelForge.Application.Features.Admin.Commands.UpdateEmailTemplate;

/// <summary>
/// PUT /api/admin/email-templates/{key} — Admin lưu mẫu một loại email hệ thống. Append-only: thêm bản ghi mới và ghi audit
/// (BR-18). Lưu lại nội dung mặc định chính là "dùng mẫu mặc định".
/// </summary>
public sealed record UpdateEmailTemplateCommand(Guid AdminUserId, string Key, string Subject, string BodyHtml)
    : IRequest<Result<AdminEmailTemplateDto>>;

public sealed class UpdateEmailTemplateCommandHandler : IRequestHandler<UpdateEmailTemplateCommand, Result<AdminEmailTemplateDto>>
{
    private readonly IPanelForgeDbContext _db;

    public UpdateEmailTemplateCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AdminEmailTemplateDto>> Handle(UpdateEmailTemplateCommand command, CancellationToken ct)
    {
        var definition = EmailTemplateCatalog.Find(command.Key);
        if (definition is null)
            return Result<AdminEmailTemplateDto>.Failure("Không có loại email này.", ResultErrorCodes.NotFound);

        PlatformEmailTemplate template;
        try
        {
            template = PlatformEmailTemplate.Create(definition.Key, command.Subject, command.BodyHtml, command.AdminUserId);
        }
        catch (ArgumentException ex)
        {
            return Result<AdminEmailTemplateDto>.Failure(ex.Message.Split(" (Parameter")[0], ResultErrorCodes.Validation);
        }

        var invalid = EmailTemplateCatalog.Validate(definition, template.Subject, template.BodyHtml);
        if (invalid is not null)
            return Result<AdminEmailTemplateDto>.Failure(invalid, ResultErrorCodes.Validation);

        var isDefault = template.Subject == definition.DefaultSubject && template.BodyHtml == definition.DefaultBodyHtml;
        _db.PlatformEmailTemplates.Add(template);
        _db.AuditLogs.Add(AuditLog.Create(
            action: "PLATFORM_EMAIL_TEMPLATE_CHANGED",
            entityName: nameof(PlatformEmailTemplate),
            entityId: template.Id.ToString(),
            userId: command.AdminUserId,
            details: isDefault
                ? $"Đưa mẫu email {definition.Key} về mặc định"
                : $"Sửa mẫu email {definition.Key}: tiêu đề \"{template.Subject}\""));
        await _db.SaveChangesAsync(ct);

        return Result<AdminEmailTemplateDto>.Success(await GetEmailTemplatesQueryHandler.LoadAsync(_db, definition, ct));
    }
}
