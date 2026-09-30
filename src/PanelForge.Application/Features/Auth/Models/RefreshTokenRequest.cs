using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.Features.Auth.Models;

public record RefreshTokenRequest(
    [Required(ErrorMessage = "Refresh token là bắt buộc.")]
    string RefreshToken
);

/// <summary>Body tuỳ chọn của POST /api/auth/logout.</summary>
public record LogoutRequest(string? RefreshToken = null);
