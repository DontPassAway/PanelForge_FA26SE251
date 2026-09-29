using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.MasterData.ElementTypes.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.MasterData.ElementTypes.Common;

namespace PanelForge.Application.Features.MasterData.ElementTypes.Queries.GetElementTypes;

public sealed record GetElementTypesQuery() : IRequest<Result<IReadOnlyList<ElementTypeDto>>>;

public sealed class GetElementTypesQueryHandler
    : IRequestHandler<GetElementTypesQuery, Result<IReadOnlyList<ElementTypeDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetElementTypesQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<ElementTypeDto>>> Handle(GetElementTypesQuery q, CancellationToken ct)
    {
        var list = await _dbContext.ElementTypes
            .OrderBy(e => e.Code)
            .ToListAsync(ct);
        return Result<IReadOnlyList<ElementTypeDto>>.Success(list.Select(e => e.ToDto()).ToList());
    }
}
