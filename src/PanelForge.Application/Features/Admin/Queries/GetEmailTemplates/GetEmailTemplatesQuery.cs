using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Email;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Admin.Models;

namespace PanelForge.Application.Features.Admin.Queries.GetEmailTemplates;

/// <summary>GET /api/admin/email-templates — nội dung đang dùng của từng loại email hệ thống, kèm nội dung mặc định.</summary>
public sealed record GetEmailTemplatesQuery : IRequest<Result<IReadOnlyList<AdminEmailTemplateDto>>>;

public sealed class GetEmailTemplatesQueryHandler
    : IRequestHandler<GetEmailTemplatesQuery, Result<IReadOnlyList<AdminEmailTemplateDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetEmailTemplatesQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<AdminEmailTemplateDto>>> Handle(GetEmailTemplatesQuery query, CancellationToken ct)
    {
        var templates = new List<AdminEmailTemplateDto>();
        foreach (var definition in EmailTemplateCatalog.All)
            templates.Add(await LoadAsync(_db, definition, ct));
        return Result<IReadOnlyList<AdminEmailTemplateDto>>.Success(templates);
    }

    internal static async Task<AdminEmailTemplateDto> LoadAsync(
        IPanelForgeDbContext db, EmailTemplateDefinition definition, CancellationToken ct)
    {
        var saved = await db.PlatformEmailTemplates.AsNoTracking()
            .Where(t => t.TemplateKey == definition.Key && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                Content = new EmailTemplateContent(t.Subject, t.Greeting, t.Intro, t.Note, t.Signature),
                t.CreatedAt,
                ChangedByName = db.Users.Where(u => u.Id == t.ChangedByUserId).Select(u => u.FullName).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        return new AdminEmailTemplateDto(
            definition.Key,
            saved?.Content ?? definition.Default,
            definition.Default,
            definition.Placeholders,
            definition.HasCode,
            saved is not null && saved.Content != definition.Default,
            saved?.CreatedAt,
            saved?.ChangedByName);
    }
}
