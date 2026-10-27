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

    /// <summary>Series chứa Chapter; null nếu Chapter không tồn tại.</summary>
    Task<Guid?> GetSeriesIdOfChapterAsync(Guid chapterId, CancellationToken cancellationToken = default);

    /// <summary>Series chứa Page (qua Chapter); null nếu Page không tồn tại.</summary>
    Task<Guid?> GetSeriesIdOfPageAsync(Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>Series chứa Panel (qua Page, Chapter); null nếu Panel không tồn tại.</summary>
    Task<Guid?> GetSeriesIdOfPanelAsync(Guid panelId, CancellationToken cancellationToken = default);

    /// <summary>Series chứa Scene (qua Chapter); null nếu Scene không tồn tại.</summary>
    Task<Guid?> GetSeriesIdOfSceneAsync(Guid sceneId, CancellationToken cancellationToken = default);

    /// <summary>Kiểm tra xem người dùng có phải là thành viên của Series hay không (Workspace Owner / Producer luôn có quyền, thành viên khác phải có trong SeriesMember).</summary>
    Task<bool> IsSeriesMemberAsync(Guid userId, Guid seriesId, CancellationToken cancellationToken = default);

    /// <summary>Kiểm tra vai trò cấp Series: Workspace Owner/Producer luôn có quyền, thành viên khác cần được chỉ định trong SeriesMember.</summary>
    Task<bool> HasSeriesRoleAsync(Guid userId, Guid seriesId, WorkspaceRole[] allowedRoles, CancellationToken cancellationToken = default);
    Task<WorkspaceRole?> GetSeriesMemberRoleAsync(Guid userId, Guid seriesId, CancellationToken cancellationToken = default);
}
