using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.ContentElements.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.ContentElements.Queries.GetElementsByPanel;

public sealed record GetContentElementsByPanelQuery(Guid PanelId) : IRequest<Result<IReadOnlyList<ContentElementDto>>>;

public sealed class GetContentElementsByPanelQueryHandler : IRequestHandler<GetContentElementsByPanelQuery, Result<IReadOnlyList<ContentElementDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetContentElementsByPanelQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<ContentElementDto>>> Handle(GetContentElementsByPanelQuery query, CancellationToken cancellationToken)
    {
        var elements = await _dbContext.Elements
            .AsNoTracking()
            .Where(e => e.PanelId == query.PanelId)
            .OrderBy(e => e.ZIndex)
            .Select(e => new ContentElementDto(
                e.Id,
                e.PanelId,
                e.ElementType,
                e.ZIndex,
                e.TransformGeometry,
                e.Content,
                e.StyleProperties,
                e.ScriptLineId,
                e.SpeakerCharacterId,
                e.CreatedAt,
                e.UpdatedAt,
                e.CreatedBy
            ))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ContentElementDto>>.Success(elements);
    }
}
