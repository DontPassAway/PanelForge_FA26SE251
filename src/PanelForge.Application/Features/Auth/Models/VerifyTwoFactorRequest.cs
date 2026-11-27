using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.Features.Auth.Models;

public record VerifyTwoFactorRequest(
    // Mã 6 số từ Authenticator, hoặc mã khôi phục ABCDE-FGHJK khi đăng nhập
    [Required(ErrorMessage = "Mã 2FA là bắt buộc.")][RegularExpression(@"^(\d{6}|[A-Za-z0-9]{5}[- ]?[A-Za-z0-9]{5})$", ErrorMessage = "Nhập mã 6 chữ số hoặc mã khôi phục dạng ABCDE-FGHJK.")] string Code,
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")] string? Email = null,
    bool RememberDevice = false,
    string? DeviceName = null
);
