namespace PanelForge.Application.Features.Workspaces.Models;

/// <summary>
/// Hạn mức cấp cho Studio mới (section <c>WorkspaceDefaults</c>, env <c>WorkspaceDefaults__StorageQuotaBytes</c>).
/// BR-22 / OPS-01: server tự đặt, không nhận số client gửi lên; Admin đổi riêng từng Studio qua
/// <c>PUT /api/admin/workspaces/{id}/quota</c>.
/// </summary>
public sealed class WorkspaceDefaultsSettings
{
    public const string SectionName = "WorkspaceDefaults";

    public long StorageQuotaBytes { get; set; } = 5L * 1024 * 1024 * 1024;
}
