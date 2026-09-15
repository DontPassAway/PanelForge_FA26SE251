using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.DTOs.Auth;

public record ChangePasswordRequest
{
    [Required(ErrorMessage = "Mật khẩu hiện tại là bắt buộc.")]
    public string CurrentPassword { get; init; } = default!;

    [Required(ErrorMessage = "Mật khẩu mới là bắt buộc.")]
    [RegularExpression(@"^(?=.*[A-Z])(?=.*[^a-zA-Z0-9]).{6,}$", 
        ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự, chứa ít nhất 1 chữ cái viết hoa và 1 ký tự đặc biệt.")]
    public string NewPassword { get; init; } = default!;

    [Required(ErrorMessage = "Xác nhận mật khẩu là bắt buộc.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; init; } = default!;
}
