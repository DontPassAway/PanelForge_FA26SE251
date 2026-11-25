namespace PanelForge.Application.Features.Auth.Models;

/// <param name="Code">Mã 6 số hiện tại từ ứng dụng Authenticator.</param>
/// <param name="CurrentPassword">Bắt buộc với tài khoản có mật khẩu; tài khoản chỉ đăng nhập mạng xã hội bỏ trống.</param>
public record DisableTwoFactorRequest(string Code, string? CurrentPassword = null);
