namespace PanelForge.Domain.Enums;

/// <summary>UC-12 / UC-14 (CF5 bước 9): vòng đời một lần xuất file, chạy nền.</summary>
public enum ExportJobStatus
{
    Queued = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}
