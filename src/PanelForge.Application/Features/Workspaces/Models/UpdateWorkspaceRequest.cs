using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.DTOs.Workspaces;

public record UpdateWorkspaceRequest
{
    [Required(ErrorMessage = "Tên Workspace là bắt buộc.")]
    [MaxLength(100, ErrorMessage = "Tên Workspace không được vượt quá 100 ký tự.")]
    public string Name { get; init; } = default!;
}
