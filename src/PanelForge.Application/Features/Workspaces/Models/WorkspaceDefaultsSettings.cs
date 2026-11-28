namespace PanelForge.Application.Features.Workspaces.Models;

/// <summary>
/// Hạn mức cấp cho Studio mới khi Admin chưa lưu chính sách nào (section <c>WorkspaceDefaults</c>,
/// env <c>WorkspaceDefaults__StorageQuotaBytes</c>...). BR-22 / OPS-01: server tự đặt, không nhận số client gửi lên.
/// OPS-20: khi đã có bản ghi <c>quota_policies</c> thì bản mới nhất thay cho các giá trị này.
/// </summary>
public sealed class WorkspaceDefaultsSettings
{
    public const string SectionName = "WorkspaceDefaults";

    public long StorageQuotaBytes { get; set; } = 5L * 1024 * 1024 * 1024;
    public long MonthlyTokenQuota { get; set; } = 1_000_000;
    /// <summary>0 = không giới hạn (giữ hành vi cũ cho tới khi Admin lưu chính sách).</summary>
    public long MonthlyImageQuota { get; set; } = 0;
}
