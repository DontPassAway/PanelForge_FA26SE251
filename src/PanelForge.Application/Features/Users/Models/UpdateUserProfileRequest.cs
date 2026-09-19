using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.DTOs.Users;

public record UpdateUserProfileRequest
{
    [Required(ErrorMessage = "Username là bắt buộc.")]
    [MaxLength(30, ErrorMessage = "Username không được vượt quá 30 ký tự.")]
    public string Username { get; init; } = default!;

    [MaxLength(2048, ErrorMessage = "Đường dẫn Avatar không được vượt quá 2048 ký tự.")]
    public string? Avatar { get; init; }

    [Phone(ErrorMessage = "Số điện thoại không đúng định dạng.")]
    public string? PhoneNumber { get; init; }
}
