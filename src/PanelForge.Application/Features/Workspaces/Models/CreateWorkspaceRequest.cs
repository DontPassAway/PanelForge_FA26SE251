using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.Features.Workspaces.Models;

public record CreateWorkspaceRequest
{
    [Required(ErrorMessage = "Tên Workspace là bắt buộc.")]
    [MaxLength(100, ErrorMessage = "Tên Workspace không được vượt quá 100 ký tự.")]
    public string Name { get; init; } = default!;

    // Quota không còn nhận từ client: lấy theo ResourcePolicy (trường storageQuotaBytes cũ gửi lên sẽ bị bỏ qua).
}
