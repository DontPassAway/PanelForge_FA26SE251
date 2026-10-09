using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.Features.Auth.Models;

public record VerifyEmailRequest(
    [Required][EmailAddress(ErrorMessage = "Email không đúng định dạng.")] string Email,
    [Required(ErrorMessage = "Mã xác thực/Token là bắt buộc.")] string Token
);
