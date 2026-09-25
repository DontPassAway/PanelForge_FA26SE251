using System.ComponentModel.DataAnnotations;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.DTOs.Workspaces;

public record AddWorkspaceMemberRequest
{
    [Required(ErrorMessage = "Email thành viên là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; init; } = default!;

    [Required(ErrorMessage = "Vai trò là bắt buộc.")]
    public WorkspaceRole Role { get; init; } = WorkspaceRole.Artist;
}
