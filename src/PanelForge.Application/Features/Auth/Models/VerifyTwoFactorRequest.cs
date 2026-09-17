using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.DTOs.Auth;

public record VerifyTwoFactorRequest(
    [Required(ErrorMessage = "Mã 6 số 2FA là bắt buộc.")][RegularExpression(@"^\d{6}$", ErrorMessage = "Mã xác thực phải bao gồm 6 chữ số.")] string Code,
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")] string? Email = null
);
