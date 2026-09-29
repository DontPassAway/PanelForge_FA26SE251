using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.MasterData.ElementTypes.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.MasterData.ElementTypes.Common;

namespace PanelForge.Application.Features.MasterData.ElementTypes.Queries.GetElementTypeById;

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
