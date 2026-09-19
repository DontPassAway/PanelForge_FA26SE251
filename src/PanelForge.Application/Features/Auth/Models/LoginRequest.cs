using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.DTOs.Auth;

public record LoginRequest(
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    string Email,

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    string Password
);
