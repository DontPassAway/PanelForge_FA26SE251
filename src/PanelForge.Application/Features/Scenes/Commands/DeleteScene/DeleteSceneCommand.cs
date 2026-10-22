using PanelForge.Application.Features.Scenes.Common;
using PanelForge.Application.Common.Interfaces.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Scenes.Commands.DeleteScene;

public sealed record DeleteSceneCommand(Guid SceneId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteSceneCommandHandler : IRequestHandler<DeleteSceneCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    private readonly IWorkspaceAuthorizationService _auth;

    public DeleteSceneCommandHandler(IPanelForgeDbContext dbContext, IWorkspaceAuthorizationService auth)
    {
        _dbContext = dbContext;
        _auth = auth;
    }

    public async Task<Result<bool>> Handle(DeleteSceneCommand command, CancellationToken cancellationToken)
    {
        var scene = await _dbContext.Scenes
            .FirstOrDefaultAsync(s => s.Id == command.SceneId, cancellationToken);

        if (scene == null)
            return Result<bool>.Failure($"Scene {command.SceneId} không tồn tại.");
        if (await ChapterEditGuard.ReadOnlyReasonAsync(_dbContext, scene.ChapterId, cancellationToken) is { } readOnly)
            return Result<bool>.Failure(readOnly, ResultErrorCodes.ChapterReadOnly);
        if (await SceneAssignmentGate.BlockedReasonAsync(_dbContext, _auth, command.UserId, scene.ChapterId, cancellationToken) is { } notAssigned)
            return Result<bool>.Failure(notAssigned, ResultErrorCodes.Forbidden);

        scene.DeletedBy = command.UserId.ToString();

        // Xóa thông thường -> DbContext tự động đổi thành Soft-delete
        _dbContext.Scenes.Remove(scene);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
