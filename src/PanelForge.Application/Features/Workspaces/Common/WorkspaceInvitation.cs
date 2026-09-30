using System.Security.Cryptography;
using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Application.Features.Workspaces.Common;

/// <summary>OTP kích hoạt cho tài khoản được mời vào Workspace (dùng chung cho mời mới và gửi lại lời mời).</summary>
internal static class WorkspaceInvitation
{
    // Cùng thời hạn với OTP quên mật khẩu
    public static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Cấp OTP 6 chữ số mới cho user (ghi đè OTP cũ, xoá bộ đếm nhập sai) và trả về mã.</summary>
    public static string IssueOtp(User user)
    {
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        user.SetPasswordResetToken(otp, DateTime.UtcNow.Add(OtpLifetime));
        return otp;
    }
}
