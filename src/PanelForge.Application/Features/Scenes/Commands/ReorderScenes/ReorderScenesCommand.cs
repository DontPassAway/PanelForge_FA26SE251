using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Scenes.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Scenes.Commands.ReorderScenes;

public sealed record ReorderScenesCommand(
    Guid ChapterId,
    Guid UserId,
    IReadOnlyList<ReorderSceneItem> Scenes
) : IRequest<Result<bool>>;

public sealed class ReorderScenesCommandHandler : IRequestHandler<ReorderScenesCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public ReorderScenesCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(ReorderScenesCommand command, CancellationToken cancellationToken)
    {
        var scenes = await _dbContext.Scenes
            .Where(s => s.ChapterId == command.ChapterId)
            .ToListAsync(cancellationToken);

        if (scenes.Count == 0)
            return Result<bool>.Failure($"Không tìm thấy Scene nào trong Chapter {command.ChapterId}.");

        var sceneMap = scenes.ToDictionary(s => s.Id);
        foreach (var item in command.Scenes)
        {
            if (sceneMap.TryGetValue(item.SceneId, out var scene))
            {
                scene.Reorder(item.NewSceneNumber);
                scene.UpdatedBy = command.UserId.ToString();
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
