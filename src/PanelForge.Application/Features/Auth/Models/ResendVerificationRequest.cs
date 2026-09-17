using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.DTOs.Auth;

public record ResendVerificationRequest(
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    string Email
);
