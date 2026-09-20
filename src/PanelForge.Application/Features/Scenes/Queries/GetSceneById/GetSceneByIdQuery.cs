using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Scenes.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Scenes.Queries.GetSceneById;

public sealed record GetSceneByIdQuery(Guid SceneId) : IRequest<Result<SceneDetailDto>>;

public sealed class GetSceneByIdQueryHandler : IRequestHandler<GetSceneByIdQuery, Result<SceneDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetSceneByIdQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SceneDetailDto>> Handle(GetSceneByIdQuery query, CancellationToken cancellationToken)
    {
        var scene = await _dbContext.Scenes
            .AsNoTracking()
            .Include(s => s.ScriptLines)
            .FirstOrDefaultAsync(s => s.Id == query.SceneId, cancellationToken);

        if (scene == null)
            return Result<SceneDetailDto>.Failure($"Scene {query.SceneId} không tồn tại.");

        var scriptLines = scene.ScriptLines
            .OrderBy(l => l.LineOrder)
            .Select(l => new SceneScriptLineSummaryDto(
                l.Id,
                l.LineOrder,
                l.DialogueText,
                l.StageDirection,
                l.SpeakerCharacterId
            ))
            .ToList();

        var detail = new SceneDetailDto(
            Id: scene.Id,
            ChapterId: scene.ChapterId,
            SceneNumber: scene.SceneNumber,
            Heading: scene.Heading,
            Summary: scene.Summary,
            CreatedAt: scene.CreatedAt,
            UpdatedAt: scene.UpdatedAt,
            CreatedBy: scene.CreatedBy,
            ScriptLines: scriptLines
        );

        return Result<SceneDetailDto>.Success(detail);
    }
}
