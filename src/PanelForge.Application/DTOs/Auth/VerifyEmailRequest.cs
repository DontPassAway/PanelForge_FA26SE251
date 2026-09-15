using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.DTOs.Auth;

public record VerifyEmailRequest(
    [Required][EmailAddress(ErrorMessage = "Email không đúng định dạng.")] string Email,
    [Required(ErrorMessage = "Mã xác thực/Token là bắt buộc.")] string Token
);
