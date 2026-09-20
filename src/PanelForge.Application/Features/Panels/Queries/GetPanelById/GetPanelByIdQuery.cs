using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Panels.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Panels.Queries.GetPanelById;

public sealed record GetPanelByIdQuery(Guid PanelId) : IRequest<Result<PanelDetailDto>>;

public sealed class GetPanelByIdQueryHandler : IRequestHandler<GetPanelByIdQuery, Result<PanelDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetPanelByIdQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PanelDetailDto>> Handle(GetPanelByIdQuery query, CancellationToken cancellationToken)
    {
        var panel = await _dbContext.Panels
            .AsNoTracking()
            .Include(p => p.Elements)
            .FirstOrDefaultAsync(p => p.Id == query.PanelId, cancellationToken);

        if (panel == null)
            return Result<PanelDetailDto>.Failure($"Panel {query.PanelId} không tồn tại.");

        var elements = panel.Elements
            .OrderBy(e => e.ZIndex)
            .Select(e => new PanelElementSummaryDto(
                e.Id,
                e.ElementType.ToString(),
                e.ZIndex,
                e.TransformGeometry,
                e.Content,
                e.StyleProperties
            ))
            .ToList();

        var detail = new PanelDetailDto(
            Id: panel.Id,
            PageId: panel.PageId,
            PanelNumber: panel.PanelNumber,
            BoundingBox: panel.BoundingBox,
            ReadingOrder: panel.ReadingOrder,
            CurrentStageId: panel.CurrentStageId,
            CreatedAt: panel.CreatedAt,
            UpdatedAt: panel.UpdatedAt,
            CreatedBy: panel.CreatedBy,
            Elements: elements
        );

        return Result<PanelDetailDto>.Success(detail);
    }
}
