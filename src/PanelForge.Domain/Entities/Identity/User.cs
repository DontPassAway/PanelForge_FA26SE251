using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Domain.Entities.Identity;

public class User : BaseEntity
{
    public string? FirebaseUid { get; private set; }
    public string Email { get; private set; } = default!;
    public string? PasswordHash { get; private set; }
    public string FullName { get; private set; } = default!;
    public string? PhoneNumber { get; private set; }
    public string? AvatarUrl { get; private set; }
    /// <summary>Bìa hồ sơ cá nhân chọn từ bộ mẫu hệ thống (null = bìa mặc định). Độc lập với avatar và bìa Series.</summary>
    public string? ProfileCoverPresetId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public SystemRole Role { get; private set; } = SystemRole.User;

    public bool IsEmailConfirmed { get; private set; } = false;

    /// <summary>Tài khoản tạo từ lời mời Workspace, chưa đặt mật khẩu và chưa xác thực email.</summary>
    public bool IsPendingInvitation => PasswordHash is null && !IsEmailConfirmed;
    public string? EmailVerificationToken { get; private set; }
    public DateTime? EmailVerificationTokenExpiresAt { get; private set; }

    public string? PasswordResetToken { get; private set; }
    public DateTime? PasswordResetTokenExpiresAt { get; private set; }

    /// <summary>Số lần nhập sai OTP (xác thực email / đặt lại mật khẩu) kể từ khi cấp OTP hiện tại.</summary>
    public int OtpFailedAttempts { get; private set; }
    public const int MaxOtpFailedAttempts = 5;

    public bool TwoFactorEnabled { get; private set; } = false;
    public string? TwoFactorSecret { get; private set; }

    /// <summary>
    /// BR-22: Cờ cấp phép tạo Studio. Chỉ Administrator mới được grant/revoke.
    /// </summary>
    public bool CanCreateStudio { get; private set; } = false;

    public ICollection<StudioWorkspace> OwnedWorkspaces { get; private set; } = [];
    public ICollection<WorkspaceMember> WorkspaceMemberships { get; private set; } = [];
    public ICollection<Assignment> AssignedTasks { get; private set; } = [];

    private User() { }

    public static User Create(
        string email,
        string fullName,
        string? passwordHash = null,
        string? firebaseUid = null,
        string? phoneNumber = null,
        string? avatarUrl = null,
        SystemRole role = SystemRole.User)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        return new User
        {
            Email = email.Trim().ToLowerInvariant(),
            FullName = fullName.Trim(),
            PasswordHash = passwordHash,
            FirebaseUid = firebaseUid?.Trim(),
            PhoneNumber = phoneNumber?.Trim(),
            AvatarUrl = avatarUrl?.Trim(),
            Role = role
        };
    }

    public void UpdateProfile(string fullName, string? phoneNumber, string? avatarUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        FullName = fullName.Trim();
        PhoneNumber = phoneNumber?.Trim();
        AvatarUrl = avatarUrl?.Trim();
    }

    /// <summary>Đặt bìa hồ sơ theo ID mẫu; null/rỗng = về bìa mặc định.</summary>
    public void SetProfileCoverPreset(string? presetId)
    {
        var id = string.IsNullOrWhiteSpace(presetId) ? null : presetId.Trim();
        if (id is not null && !ProfileCoverPresets.IsValid(id))
            throw new ArgumentException($"Mẫu ảnh bìa '{id}' không tồn tại.", nameof(presetId));
        ProfileCoverPresetId = id;
    }

    public void UpdatePassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public void SetEmailVerificationToken(string token, DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        EmailVerificationToken = token.Trim();
        EmailVerificationTokenExpiresAt = expiresAt;
        OtpFailedAttempts = 0;
    }

    public void ConfirmEmail()
    {
        IsEmailConfirmed = true;
        EmailVerificationToken = null;
        EmailVerificationTokenExpiresAt = null;
        OtpFailedAttempts = 0;
    }

    public void SetPasswordResetToken(string token, DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        PasswordResetToken = token.Trim();
        PasswordResetTokenExpiresAt = expiresAt;
        OtpFailedAttempts = 0;
    }

    public void ResetPassword(string newPasswordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);
        PasswordHash = newPasswordHash;
        PasswordResetToken = null;
        PasswordResetTokenExpiresAt = null;
        OtpFailedAttempts = 0;
    }

    /// <summary>Ghi nhận một lần nhập sai OTP; đủ 5 lần thì huỷ các OTP đang có, phải xin mã mới.</summary>
    public void RegisterFailedOtp()
    {
        if (++OtpFailedAttempts < MaxOtpFailedAttempts) return;
        EmailVerificationToken = null;
        EmailVerificationTokenExpiresAt = null;
        PasswordResetToken = null;
        PasswordResetTokenExpiresAt = null;
        OtpFailedAttempts = 0;
    }

    public void SetTwoFactorSecret(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        TwoFactorSecret = secret.Trim();
    }

    public void EnableTwoFactor()
    {
        if (string.IsNullOrWhiteSpace(TwoFactorSecret))
            throw new InvalidOperationException("Cannot enable 2FA without setting a secret key first.");

        TwoFactorEnabled = true;
    }

    public void DisableTwoFactor()
    {
        TwoFactorEnabled = false;
        TwoFactorSecret = null;
    }

    public void LinkFirebase(string firebaseUid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firebaseUid);
        FirebaseUid = firebaseUid.Trim();
    }

    public void AssignSystemRole(SystemRole newRole) => Role = newRole;

    public void Deactivate() => IsActive = false;
    public void Activate()   => IsActive = true;

    /// <summary>BR-22: Administrator grants permission to create a Studio.</summary>
    public void GrantStudioCreation() => CanCreateStudio = true;

    /// <summary>BR-22: Administrator revokes permission to create a Studio.</summary>
    public void RevokeStudioCreation() => CanCreateStudio = false;
}
