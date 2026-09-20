using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Scenes.Models;
using PanelForge.Application.Interfaces.Persistence;
using DomainScene = PanelForge.Domain.Entities.Content.Scene;

namespace PanelForge.Application.Features.Scenes.Commands.CreateScene;

public sealed record CreateSceneCommand(
    Guid ChapterId,
    Guid UserId,
    int? SceneNumber = null,
    string? Heading = null,
    string? Summary = null
) : IRequest<Result<SceneDto>>;

public sealed class CreateSceneCommandHandler : IRequestHandler<CreateSceneCommand, Result<SceneDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public CreateSceneCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SceneDto>> Handle(CreateSceneCommand command, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra Chapter có tồn tại không
        var chapterExists = await _dbContext.Chapters
            .AnyAsync(c => c.Id == command.ChapterId, cancellationToken);

        if (!chapterExists)
            return Result<SceneDto>.Failure($"Chapter {command.ChapterId} không tồn tại.");

        // 2. Tính số scene tự động nếu không truyền vào
        int sceneNumber;
        if (command.SceneNumber.HasValue && command.SceneNumber.Value > 0)
        {
            sceneNumber = command.SceneNumber.Value;
            var isDuplicate = await _dbContext.Scenes
                .AnyAsync(s => s.ChapterId == command.ChapterId && s.SceneNumber == sceneNumber, cancellationToken);

            if (isDuplicate)
                return Result<SceneDto>.Failure($"Scene số {sceneNumber} đã tồn tại trong Chapter này.");
        }
        else
        {
            var maxScene = await _dbContext.Scenes
                .Where(s => s.ChapterId == command.ChapterId)
                .MaxAsync(s => (int?)s.SceneNumber, cancellationToken);

            sceneNumber = (maxScene ?? 0) + 1;
        }

        // 3. Khởi tạo Scene
        DomainScene scene;
        try
        {
            scene = DomainScene.Create(
                chapterId: command.ChapterId,
                sceneNumber: sceneNumber,
                heading: command.Heading,
                summary: command.Summary
            );
            scene.CreatedBy = command.UserId.ToString();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result<SceneDto>.Failure(ex.Message);
        }

        _dbContext.Scenes.Add(scene);
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
            ScriptLinesCount: 0,
            PagesCount: 0
        ));
    }
}
