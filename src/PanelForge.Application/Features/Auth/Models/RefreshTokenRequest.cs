namespace PanelForge.Application.Features.Auth.Models;

/// <summary>Body tuỳ chọn của POST /api/auth/refresh; thiếu thì lấy từ cookie HttpOnly <c>pf_refresh</c>.</summary>
public record RefreshTokenRequest(string? RefreshToken = null);

/// <summary>Body tuỳ chọn của POST /api/auth/logout.</summary>
public record LogoutRequest(string? RefreshToken = null);
