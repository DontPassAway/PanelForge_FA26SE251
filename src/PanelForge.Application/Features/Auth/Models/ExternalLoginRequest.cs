using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.Features.Auth.Models;

public record ExternalLoginRequest(
    [Required(ErrorMessage = "IdToken là bắt buộc.")] string IdToken,
    string? Provider = "firebase"
);
