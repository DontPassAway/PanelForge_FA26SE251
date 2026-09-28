using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.DTOs.Content;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Pages.GetPagesByChapter;

public sealed class GetPagesByChapterQueryHandler
    : IRequestHandler<GetPagesByChapterQuery, Result<IReadOnlyList<PageDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetPagesByChapterQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<PageDto>>> Handle(
        GetPagesByChapterQuery query,
        CancellationToken cancellationToken)
    {
        var chapterExists = await _dbContext.Chapters
            .AnyAsync(c => c.Id == query.ChapterId, cancellationToken);

        if (!chapterExists)
            return Result<IReadOnlyList<PageDto>>.Failure($"Chapter {query.ChapterId} không tồn tại.");

        var pages = await _dbContext.Pages
            .Where(p => p.ChapterId == query.ChapterId)
            .OrderBy(p => p.PageNumber)
            .Select(p => new PageDto(
                p.Id,
                p.ChapterId,
                p.SceneId,
                p.PageNumber,
                p.LayoutFormat.ToString(),
                p.WidthPx,
                p.HeightPx,
                p.Dpi,
                p.Panels.Count
            ))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PageDto>>.Success(pages);
    }
}
