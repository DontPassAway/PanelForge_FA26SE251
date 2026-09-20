using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Scenes.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Scenes.Commands.UpdateScene;

public sealed record UpdateSceneCommand(
    Guid SceneId,
    Guid UserId,
    string? Heading,
    string? Summary
) : IRequest<Result<SceneDto>>;

public sealed class UpdateSceneCommandHandler : IRequestHandler<UpdateSceneCommand, Result<SceneDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateSceneCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SceneDto>> Handle(UpdateSceneCommand command, CancellationToken cancellationToken)
    {
        var scene = await _dbContext.Scenes
            .Include(s => s.ScriptLines)
            .Include(s => s.Pages)
            .FirstOrDefaultAsync(s => s.Id == command.SceneId, cancellationToken);

        if (scene == null)
            return Result<SceneDto>.Failure($"Scene {command.SceneId} không tồn tại.");

        scene.UpdateContent(command.Heading, command.Summary);
        scene.UpdatedBy = command.UserId.ToString();

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<SceneDto>.Success(new SceneDto(
            Id: scene.Id,
            ChapterId: scene.ChapterId,
            SceneNumber: scene.SceneNumber,
            Heading: scene.Heading,
            Summary: scene.Summary,
            CreatedAt: scene.CreatedAt,
            UpdatedAt: scene.UpdatedAt,
            CreatedBy: scene.CreatedBy,
            ScriptLinesCount: scene.ScriptLines.Count,
            PagesCount: scene.Pages.Count
        ));
    }
}
