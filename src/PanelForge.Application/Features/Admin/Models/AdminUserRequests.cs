using System.ComponentModel.DataAnnotations;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.DTOs.Auth;

public record CreateUserByAdminRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; init; } = default!;

    [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
    public string FullName { get; init; } = default!;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
    public string Password { get; init; } = default!;

    public string? PhoneNumber { get; init; }

    public SystemRole Role { get; init; } = SystemRole.User;

    public bool IsActive { get; init; } = true;
}

public record UpdateUserByAdminRequest
{
    [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
    public string FullName { get; init; } = default!;

    public string? PhoneNumber { get; init; }

    public SystemRole Role { get; init; } = SystemRole.User;

    public bool IsActive { get; init; } = true;
}

public record ResetPasswordByAdminRequest
{
    [Required(ErrorMessage = "Mật khẩu mới là bắt buộc.")]
    [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
    public string NewPassword { get; init; } = default!;
}
