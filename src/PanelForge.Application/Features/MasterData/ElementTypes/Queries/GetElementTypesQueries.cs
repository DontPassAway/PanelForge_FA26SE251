using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.MasterData.ElementTypes.Commands;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.MasterData.ElementTypes.Queries;

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

public sealed record GetElementTypeByIdQuery(Guid Id) : IRequest<Result<ElementTypeDto>>;

public sealed class GetElementTypeByIdQueryHandler
    : IRequestHandler<GetElementTypeByIdQuery, Result<ElementTypeDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetElementTypeByIdQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<ElementTypeDto>> Handle(GetElementTypeByIdQuery q, CancellationToken ct)
    {
        var e = await _dbContext.ElementTypes.FirstOrDefaultAsync(x => x.Id == q.Id, ct);
        if (e is null) return Result<ElementTypeDto>.Failure("ElementType không tồn tại.");
        return Result<ElementTypeDto>.Success(e.ToDto());
    }
}
