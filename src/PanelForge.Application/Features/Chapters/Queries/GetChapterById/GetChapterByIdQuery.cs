using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Chapters.Queries.GetChapterById;

public sealed record GetChapterByIdQuery(Guid ChapterId) : IRequest<Result<ChapterDetailDto>>;

public sealed class GetChapterByIdQueryHandler : IRequestHandler<GetChapterByIdQuery, Result<ChapterDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetChapterByIdQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ChapterDetailDto>> Handle(GetChapterByIdQuery query, CancellationToken cancellationToken)
    {
        var chapter = await _dbContext.Chapters
            .AsNoTracking()
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == query.ChapterId, cancellationToken);

        if (chapter == null)
            return Result<ChapterDetailDto>.Failure($"Chapter {query.ChapterId} không tồn tại.");

        var scenes = chapter.Scenes
            .OrderBy(s => s.SceneNumber)
            .Select(s => new ChapterSceneSummaryDto(
                s.Id,
                s.SceneNumber,
                s.Heading,
                s.Summary
            ))
            .ToList();

        var pages = chapter.Pages
            .OrderBy(p => p.PageNumber)
            .Select(p => new ChapterPageSummaryDto(
                p.Id,
                p.PageNumber,
                p.LayoutFormat.ToString(),
                p.WidthPx,
                p.HeightPx,
                p.Dpi
            ))
            .ToList();

        var detail = new ChapterDetailDto(
            Id: chapter.Id,
            SeriesId: chapter.SeriesId,
            ChapterNumber: chapter.ChapterNumber,
            Title: chapter.Title,
            TargetReleaseDate: chapter.TargetReleaseDate,
            VersionVector: chapter.VersionVector,
            CreatedAt: chapter.CreatedAt,
            UpdatedAt: chapter.UpdatedAt,
            CreatedBy: chapter.CreatedBy,
            Scenes: scenes,
            Pages: pages
        );

        return Result<ChapterDetailDto>.Success(detail);
    }
}
