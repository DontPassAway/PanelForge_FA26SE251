using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.DTOs.Content;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Pages.GetPageHierarchy;

public sealed record GetPageHierarchyQuery(Guid PageId) : IRequest<Result<PageContentHierarchyDto>>;

public sealed class GetPageHierarchyQueryHandler : IRequestHandler<GetPageHierarchyQuery, Result<PageContentHierarchyDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetPageHierarchyQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PageContentHierarchyDto>> Handle(GetPageHierarchyQuery query, CancellationToken cancellationToken)
    {
        var page = await _dbContext.Pages
            .AsNoTracking()
            .Include(p => p.Panels)
                .ThenInclude(p => p.Elements)
            .FirstOrDefaultAsync(p => p.Id == query.PageId, cancellationToken);

        if (page == null)
            return Result<PageContentHierarchyDto>.Failure($"Page {query.PageId} không tồn tại.");

        var panels = page.Panels
            .OrderBy(p => p.ReadingOrder)
            .Select(p => new PagePanelDetailDto(
                Id: p.Id,
                PageId: p.PageId,
                PanelNumber: p.PanelNumber,
                BoundingBox: p.BoundingBox,
                ReadingOrder: p.ReadingOrder,
                CurrentStageId: p.CurrentStageId,
                CreatedAt: p.CreatedAt,
                Elements: p.Elements
                    .OrderBy(e => e.ZIndex)
                    .Select(e => new PageElementDetailDto(
                        Id: e.Id,
                        PanelId: e.PanelId,
                        ElementType: e.ElementType.ToString(),
                        ZIndex: e.ZIndex,
                        TransformGeometry: e.TransformGeometry,
                        Content: e.Content,
                        StyleProperties: e.StyleProperties,
                        ScriptLineId: e.ScriptLineId,
                        SpeakerCharacterId: e.SpeakerCharacterId,
                        CreatedAt: e.CreatedAt
                    ))
                    .ToList()
            ))
            .ToList();

        var hierarchy = new PageContentHierarchyDto(
            Id: page.Id,
            ChapterId: page.ChapterId,
            SceneId: page.SceneId,
            PageNumber: page.PageNumber,
            LayoutFormat: page.LayoutFormat.ToString(),
            WidthPx: page.WidthPx,
            HeightPx: page.HeightPx,
            Dpi: page.Dpi,
            CreatedAt: page.CreatedAt,
            UpdatedAt: page.UpdatedAt,
            Panels: panels
        );

        return Result<PageContentHierarchyDto>.Success(hierarchy);
    }
}
