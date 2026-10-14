using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Scenes.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Scenes.Queries.GetScenesByChapter;

public sealed record GetScenesByChapterQuery(Guid ChapterId) : IRequest<Result<IReadOnlyList<SceneDto>>>;

public sealed class GetScenesByChapterQueryHandler : IRequestHandler<GetScenesByChapterQuery, Result<IReadOnlyList<SceneDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetScenesByChapterQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<SceneDto>>> Handle(GetScenesByChapterQuery query, CancellationToken cancellationToken)
    {
        var scenes = await _dbContext.Scenes
            .AsNoTracking()
            .Where(s => s.ChapterId == query.ChapterId)
            .OrderBy(s => s.SceneNumber)
            .Select(s => new SceneDto(
                s.Id,
                s.ChapterId,
                s.SceneNumber,
                s.Heading,
                s.Summary,
                s.CreatedAt,
                s.UpdatedAt,
                s.CreatedBy,
                s.ScriptLines.Count,
                s.Panels.Select(p => p.PageId).Distinct().Count(),  // số Page có Panel của Scene (Scene không chứa Page)
                s.DefaultNarrationMode
            ))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SceneDto>>.Success(scenes);
    }
}
