using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.Features.Auth.Models;

public record ForgotPasswordRequest(
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    string Email
);
