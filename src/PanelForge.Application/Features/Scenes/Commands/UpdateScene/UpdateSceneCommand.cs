using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Scenes.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Scenes.Commands.UpdateScene;

public sealed record UpdateSceneCommand(
    Guid SceneId,
    Guid UserId,
    string? Heading,
    string? Summary,
    PanelForge.Domain.Enums.NarrationMode? DefaultNarrationMode = null,
    int? ExpectedVersion = null   // BR-11: version client đã thấy; null = không kiểm
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
            .Include(s => s.Panels)
            .FirstOrDefaultAsync(s => s.Id == command.SceneId, cancellationToken);

        if (scene == null)
            return Result<SceneDto>.Failure($"Scene {command.SceneId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_dbContext, scene.ChapterId, cancellationToken) is { } readOnly)
            return Result<SceneDto>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
        if (command.ExpectedVersion is { } expected && expected != scene.Version)
            return Result<SceneDto>.Failure(
                $"Scene đã được người khác sửa (version hiện tại {scene.Version}, bạn đang sửa bản {expected}). Tải lại rồi thử lại.",
                ResultErrorCodes.Conflict);

        scene.UpdateContent(command.Heading, command.Summary, command.DefaultNarrationMode);
        scene.UpdatedBy = command.UserId.ToString();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Hai người lưu gần như cùng lúc: Version là concurrency token nên bản lưu sau bị từ chối (BR-11)
            return Result<SceneDto>.Failure("Scene vừa được người khác sửa. Tải lại rồi thử lại.", ResultErrorCodes.Conflict);
        }

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
            PagesCount: scene.Panels.Select(p => p.PageId).Distinct().Count(),
            DefaultNarrationMode: scene.DefaultNarrationMode,
            Version: scene.Version
        ));
    }
}
