using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.DTOs.Auth;

public record RegisterRequest(
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    string Email,

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [RegularExpression(@"^(?=.*[A-Z])(?=.*[^a-zA-Z0-9]).{6,}$", 
        ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự, chứa ít nhất 1 chữ cái viết hoa và 1 ký tự đặc biệt.")]
    string Password,

    [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
    [MaxLength(100, ErrorMessage = "Họ và tên không được vượt quá 100 ký tự.")]
    string FullName,

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    string? PhoneNumber = null
);
