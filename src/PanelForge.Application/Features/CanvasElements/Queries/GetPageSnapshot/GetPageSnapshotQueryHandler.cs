using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces;

namespace PanelForge.Application.Features.Elements.GetPageSnapshot;

public sealed class GetPageSnapshotQueryHandler
    : IRequestHandler<GetPageSnapshotQuery, Result<PageSnapshotDto>>
{
    private readonly IPageRepository _pageRepository;

    public GetPageSnapshotQueryHandler(IPageRepository pageRepository)
    {
        _pageRepository = pageRepository;
    }

    public async Task<Result<PageSnapshotDto>> Handle(
        GetPageSnapshotQuery query,
        CancellationToken cancellationToken)
    {
        var page = await _pageRepository.GetAsync(query.PageId, cancellationToken);
        if (page is null)
            return Result<PageSnapshotDto>.Failure(
                $"Page {query.PageId} không tồn tại.");

        // Map ElementState → DTO, lọc theo IncludeRemoved
        var elements = page.Elements.Values
            .Where(e => query.IncludeRemoved || !e.IsRemoved)
            .OrderBy(e => e.ZIndex)
            .Select(e => new ElementSnapshotDto(
                ElementId:   e.ElementId,
                ElementType: e.ElementType,
                ZIndex:      e.ZIndex,
                X:           e.X,
                Y:           e.Y,
                Width:       e.Width,
                Height:      e.Height,
                Content:     e.Content,
                AssetId:     e.AssetId,
                IsRemoved:   e.IsRemoved
            ))
            .ToList();

        return Result<PageSnapshotDto>.Success(new PageSnapshotDto(
            PageId:     page.Id,
            Version:    page.Version,
            PageNumber: page.PageNumber,
            Elements:   elements
        ));
    }
}
