using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Panels.Queries.GetPanelsByPage;

public sealed record GetPanelsByPageQuery(Guid PageId) : IRequest<Result<IReadOnlyList<PanelDto>>>;

public sealed class GetPanelsByPageQueryHandler : IRequestHandler<GetPanelsByPageQuery, Result<IReadOnlyList<PanelDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetPanelsByPageQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<PanelDto>>> Handle(GetPanelsByPageQuery query, CancellationToken cancellationToken)
    {
        var panels = await _dbContext.Panels
            .AsNoTracking()
            .Where(p => p.PageId == query.PageId)
            .OrderBy(p => p.ReadingOrder)
            .Select(p => new PanelDto(
                p.Id,
                p.PageId,
                p.PanelNumber,
                p.BoundingBox,
                p.ReadingOrder,
                p.CurrentStageId,
                p.CreatedAt,
                p.UpdatedAt,
                p.CreatedBy,
                p.Elements.Count
            ))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PanelDto>>.Success(panels);
    }
}
