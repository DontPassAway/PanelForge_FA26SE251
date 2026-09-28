using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.DTOs.Content;
using PanelForge.Application.Interfaces;

namespace PanelForge.Application.Features.Pages.GetPageById;

public sealed class GetPageByIdQueryHandler
    : IRequestHandler<GetPageByIdQuery, Result<PageDetailDto>>
{
    private readonly IPageRepository _pageRepository;

    public GetPageByIdQueryHandler(IPageRepository pageRepository)
    {
        _pageRepository = pageRepository;
    }

    public async Task<Result<PageDetailDto>> Handle(
        GetPageByIdQuery query,
        CancellationToken cancellationToken)
    {
        var page = await _pageRepository.GetAsync(query.PageId, cancellationToken);
        if (page is null)
            return Result<PageDetailDto>.Failure($"Page {query.PageId} không tồn tại.");

        var activeElements = page.Elements.Values.Count(e => !e.IsRemoved);

        return Result<PageDetailDto>.Success(new PageDetailDto(
            Id:                  page.Id,
            ChapterId:           page.ChapterId,
            SceneId:             page.SceneId,
            PageNumber:          page.PageNumber,
            LayoutFormat:        page.LayoutFormat.ToString(),
            WidthPx:             page.WidthPx,
            HeightPx:            page.HeightPx,
            Dpi:                 page.Dpi,
            Version:             page.Version,
            ActiveElementsCount: activeElements
        ));
    }
}
