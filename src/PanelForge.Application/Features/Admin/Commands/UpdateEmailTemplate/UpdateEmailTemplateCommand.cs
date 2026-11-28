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
/// PUT /api/admin/email-templates/{key} — Admin lưu nội dung một loại email hệ thống. Append-only: thêm bản ghi mới và
/// ghi audit (BR-18). Lưu lại nội dung mặc định chính là "dùng mẫu mặc định".
/// </summary>
public sealed record UpdateEmailTemplateCommand(Guid AdminUserId, string Key, EmailTemplateContent Content)
    : IRequest<Result<AdminEmailTemplateDto>>;

public sealed class UpdateEmailTemplateCommandHandler : IRequestHandler<UpdateEmailTemplateCommand, Result<AdminEmailTemplateDto>>
{
    private readonly IPanelForgeDbContext _db;

    public UpdateEmailTemplateCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AdminEmailTemplateDto>> Handle(UpdateEmailTemplateCommand command, CancellationToken ct)
    {
        var draft = EmailTemplateDraft.Parse(command.AdminUserId, command.Key, command.Content);
        if (!draft.IsSuccess)
            return Result<AdminEmailTemplateDto>.Failure(draft.ErrorMessage!, draft.ErrorCode);
        var (definition, template) = draft.Value!;

        var isDefault = EmailTemplateDraft.ContentOf(template) == definition.Default;
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

/// <summary>Kiểm tra nội dung Admin nhập (dùng chung cho lưu, xem trước, gửi thử) và chuẩn hoá qua entity.</summary>
internal static class EmailTemplateDraft
{
    public static Result<(EmailTemplateDefinition Definition, PlatformEmailTemplate Template)> Parse(
        Guid adminUserId, string key, EmailTemplateContent? content)
    {
        var definition = EmailTemplateCatalog.Find(key);
        if (definition is null)
            return Result<(EmailTemplateDefinition, PlatformEmailTemplate)>.Failure("Không có loại email này.", ResultErrorCodes.NotFound);
        if (content is null)
            return Result<(EmailTemplateDefinition, PlatformEmailTemplate)>.Failure("Thiếu nội dung email.", ResultErrorCodes.Validation);

        PlatformEmailTemplate template;
        try
        {
            template = PlatformEmailTemplate.Create(
                definition.Key, content.Subject, content.Greeting, content.Intro, content.Note, content.Signature, adminUserId);
        }
        catch (ArgumentException ex)
        {
            return Result<(EmailTemplateDefinition, PlatformEmailTemplate)>.Failure(
                ex.Message.Split(" (Parameter")[0], ResultErrorCodes.Validation);
        }

        var invalid = EmailTemplateCatalog.Validate(definition, ContentOf(template));
        return invalid is null
            ? Result<(EmailTemplateDefinition, PlatformEmailTemplate)>.Success((definition, template))
            : Result<(EmailTemplateDefinition, PlatformEmailTemplate)>.Failure(invalid, ResultErrorCodes.Validation);
    }

    public static EmailTemplateContent ContentOf(PlatformEmailTemplate t) =>
        new(t.Subject, t.Greeting, t.Intro, t.Note, t.Signature);
}
