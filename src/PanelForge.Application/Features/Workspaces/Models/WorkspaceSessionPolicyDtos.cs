namespace PanelForge.Application.Features.Workspaces.Models;

/// <summary>Session policy của Workspace; null = không giới hạn ở Workspace này.</summary>
public sealed record WorkspaceSessionPolicyDto(
    Guid WorkspaceId,
    int? SessionLifetimeMinutes,
    int? IdleTimeoutMinutes,
    int? MaxConcurrentSessions);

/// <summary>PUT thay toàn bộ policy: field nào null/không gửi nghĩa là bỏ giới hạn đó.</summary>
public sealed record UpdateWorkspaceSessionPolicyRequest(
    int? SessionLifetimeMinutes = null,
    int? IdleTimeoutMinutes = null,
    int? MaxConcurrentSessions = null);
