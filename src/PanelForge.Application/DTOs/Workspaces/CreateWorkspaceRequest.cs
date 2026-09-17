using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.DTOs.Workspaces;

public record CreateWorkspaceRequest
{
    [Required(ErrorMessage = "Tên Workspace là bắt buộc.")]
    [MaxLength(100, ErrorMessage = "Tên Workspace không được vượt quá 100 ký tự.")]
    public string Name { get; init; } = default!;

    [Range(0, long.MaxValue, ErrorMessage = "Dung lượng lưu trữ không hợp lệ.")]
    public long StorageQuotaBytes { get; init; } = 5L * 1024 * 1024 * 1024; // 5GB mặc định
}
