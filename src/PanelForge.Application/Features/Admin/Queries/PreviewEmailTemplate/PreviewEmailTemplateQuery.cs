using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Email;
using PanelForge.Application.Features.Admin.Commands.UpdateEmailTemplate;
using PanelForge.Application.Features.Admin.Models;

namespace PanelForge.Application.Features.Admin.Queries.PreviewEmailTemplate;

/// <summary>
/// POST /api/admin/email-templates/{key}/preview — dựng email từ nội dung đang soạn với dữ liệu mẫu, đúng bố cục khi gửi.
/// Không lưu gì.
/// </summary>
public sealed record PreviewEmailTemplateQuery(string Key, EmailTemplateContent Content) : IRequest<Result<EmailTemplatePreviewDto>>;

public sealed class PreviewEmailTemplateQueryHandler : IRequestHandler<PreviewEmailTemplateQuery, Result<EmailTemplatePreviewDto>>
{
    public const string SampleRecipient = "Nguyễn Văn An";

    public Task<Result<EmailTemplatePreviewDto>> Handle(PreviewEmailTemplateQuery query, CancellationToken ct)
    {
        var draft = EmailTemplateDraft.Parse(Guid.Empty, query.Key, query.Content);
        if (!draft.IsSuccess)
            return Task.FromResult(Result<EmailTemplatePreviewDto>.Failure(draft.ErrorMessage!, draft.ErrorCode));

        var (definition, template) = draft.Value!;
        var (subject, body) = EmailTemplateCatalog.Render(
            definition, EmailTemplateDraft.ContentOf(template), EmailTemplateCatalog.SampleValues(SampleRecipient));
        return Task.FromResult(Result<EmailTemplatePreviewDto>.Success(new EmailTemplatePreviewDto(subject, body)));
    }
}
