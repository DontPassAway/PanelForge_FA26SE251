using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.FrameTemplates.Models;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.FrameTemplates.Queries.GetFrameTemplates;

/// <summary>Template có sẵn của hệ thống trước, rồi template riêng của Workspace (mới nhất trước).</summary>
public sealed record GetFrameTemplatesQuery(Guid WorkspaceId) : IRequest<IReadOnlyList<FrameTemplateDto>>;

public sealed class GetFrameTemplatesQueryHandler : IRequestHandler<GetFrameTemplatesQuery, IReadOnlyList<FrameTemplateDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetFrameTemplatesQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<IReadOnlyList<FrameTemplateDto>> Handle(GetFrameTemplatesQuery q, CancellationToken ct)
    {
        var custom = await _db.FrameTemplates.AsNoTracking()
            .Where(t => t.WorkspaceId == q.WorkspaceId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return FrameTemplate.BuiltIns.Concat(custom).Select(FrameTemplateDto.From).ToList();
    }
}
