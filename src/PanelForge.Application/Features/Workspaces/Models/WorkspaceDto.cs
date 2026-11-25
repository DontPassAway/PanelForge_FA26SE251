using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Models;

public record WorkspaceDto(
    Guid Id,
    string Name,
    Guid OwnerId,
    string OwnerName,
    string OwnerEmail,
    long StorageQuotaBytes,
    long UsedStorageBytes,
    DateTime CreatedAt,
    bool IsOwner,
    WorkspaceRole? CurrentUserRole,
    int MemberCount,
    int PendingTasks = 0,
    string? CoverUrl = null,
    string? Description = null,
    IReadOnlyList<WorkspaceMemberPreviewDto>? MemberPreviews = null,
    int PendingEditorReviews = 0,
    int SeriesCount = 0,
    IReadOnlyList<WorkspaceSeriesPreviewDto>? SeriesPreviews = null,
    /// <summary>Lần cập nhật gần nhất của series, chương hoặc việc trong Studio; null = chưa có hoạt động.</summary>
    DateTime? LastActivityAt = null
);

/// <summary>Series cập nhật gần đây nhất của Studio, để hiện ảnh bìa nhỏ trên thẻ Studio.</summary>
public sealed record WorkspaceSeriesPreviewDto(Guid Id, string Title, string? CoverUrl);
