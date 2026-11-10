using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Publisher.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Queries.GetPublisherWorkspaces;

public sealed record GetPublisherWorkspacesQuery() : IRequest<Result<IReadOnlyList<PublisherWorkspaceDto>>>;

public sealed class GetPublisherWorkspacesQueryHandler
    : IRequestHandler<GetPublisherWorkspacesQuery, Result<IReadOnlyList<PublisherWorkspaceDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetPublisherWorkspacesQueryHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<PublisherWorkspaceDto>>> Handle(
        GetPublisherWorkspacesQuery query,
        CancellationToken ct)
    {
        var list = await _db.StudioWorkspaces
            .AsNoTracking()
            .Where(w => !w.IsDeleted)
            .OrderBy(w => w.Name)
            .Select(w => new PublisherWorkspaceDto(
                w.Id,
                w.Name,
                w.Series.Count,
                w.Series.SelectMany(s => s.Chapters).Count(c => c.Status == ChapterStatus.Approved)
            ))
            .ToListAsync(ct);

        return Result<IReadOnlyList<PublisherWorkspaceDto>>.Success(list);
    }
}
