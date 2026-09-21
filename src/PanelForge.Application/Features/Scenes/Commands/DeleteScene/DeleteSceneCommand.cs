using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Scenes.Commands.DeleteScene;

public sealed record DeleteSceneCommand(Guid SceneId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteSceneCommandHandler : IRequestHandler<DeleteSceneCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public DeleteSceneCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(DeleteSceneCommand command, CancellationToken cancellationToken)
    {
        var scene = await _dbContext.Scenes
            .FirstOrDefaultAsync(s => s.Id == command.SceneId, cancellationToken);

        if (scene == null)
            return Result<bool>.Failure($"Scene {command.SceneId} không tồn tại.");

        scene.DeletedBy = command.UserId.ToString();

        // Xóa thông thường -> DbContext tự động đổi thành Soft-delete
        _dbContext.Scenes.Remove(scene);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
