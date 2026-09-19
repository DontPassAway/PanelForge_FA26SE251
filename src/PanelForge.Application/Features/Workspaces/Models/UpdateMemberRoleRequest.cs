using System.ComponentModel.DataAnnotations;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.DTOs.Workspaces;

public record UpdateMemberRoleRequest
{
    [Required(ErrorMessage = "Vai trò mới là bắt buộc.")]
    public WorkspaceRole NewRole { get; init; }
}
