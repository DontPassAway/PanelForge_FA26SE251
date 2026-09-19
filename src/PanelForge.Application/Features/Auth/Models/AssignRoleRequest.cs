using System.ComponentModel.DataAnnotations;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.DTOs.Auth;

public record AssignRoleRequest(
    [Required(ErrorMessage = "UserId là bắt buộc.")] Guid UserId,
    [Required(ErrorMessage = "Role là bắt buộc.")] SystemRole NewRole
);
