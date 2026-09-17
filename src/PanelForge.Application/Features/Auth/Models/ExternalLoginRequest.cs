using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.DTOs.Auth;

public record ExternalLoginRequest(
    [Required(ErrorMessage = "IdToken là bắt buộc.")] string IdToken,
    string? Provider = "firebase"
);
