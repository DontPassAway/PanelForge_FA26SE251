using PanelForge.Domain.Enums;

namespace PanelForge.Application.Common.Interfaces.Services;

public interface IWorkspaceAuthorizationService
{
    Task<bool> IsOwnerAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<bool> IsOwnerOrProducerAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<bool> HasWorkspaceRoleAsync(Guid userId, Guid workspaceId, WorkspaceRole[] allowedRoles, CancellationToken cancellationToken = default);
    Task<bool> IsMemberAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceRole?> GetMemberRoleAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Workspace chứa Series; null nếu Series không tồn tại.</summary>
    Task<Guid?> GetWorkspaceIdOfSeriesAsync(Guid seriesId, CancellationToken cancellationToken = default);

    /// <summary>Workspace chứa Chapter (qua Series); null nếu Chapter không tồn tại.</summary>
    Task<Guid?> GetWorkspaceIdOfChapterAsync(Guid chapterId, CancellationToken cancellationToken = default);

    /// <summary>Workspace chứa Page (qua Chapter, Series); null nếu Page không tồn tại.</summary>
    Task<Guid?> GetWorkspaceIdOfPageAsync(Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>Workspace chứa Panel (qua Page, Chapter, Series); null nếu Panel không tồn tại.</summary>
    Task<Guid?> GetWorkspaceIdOfPanelAsync(Guid panelId, CancellationToken cancellationToken = default);
    Task<Guid?> GetWorkspaceIdOfSceneAsync(Guid sceneId, CancellationToken cancellationToken = default);
}
