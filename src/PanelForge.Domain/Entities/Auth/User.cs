using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Auth;

public class User : BaseEntity
{
    public string? FirebaseUid { get; private set; }
    public string Email { get; private set; } = default!;
    public string? PasswordHash { get; private set; }
    public string FullName { get; private set; } = default!;
    public string? PhoneNumber { get; private set; }
    public string? AvatarUrl { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public bool IsActive { get; private set; } = true;
    public SystemRole Role { get; private set; } = SystemRole.User;

    public bool IsEmailConfirmed { get; private set; } = false;
    public string? EmailVerificationToken { get; private set; }
    public DateTime? EmailVerificationTokenExpiresAt { get; private set; }

    public string? PasswordResetToken { get; private set; }
    public DateTime? PasswordResetTokenExpiresAt { get; private set; }

    public bool TwoFactorEnabled { get; private set; } = false;
    public string? TwoFactorSecret { get; private set; }

    public ICollection<StudioWorkspace> OwnedWorkspaces { get; private set; } = [];
    public ICollection<WorkspaceMember> WorkspaceMemberships { get; private set; } = [];
    public ICollection<ExternalPreviewLink> CreatedPreviewLinks { get; private set; } = [];
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
    }

    public void ConfirmEmail()
    {
        IsEmailConfirmed = true;
        EmailVerificationToken = null;
        EmailVerificationTokenExpiresAt = null;
    }

    public void SetPasswordResetToken(string token, DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        PasswordResetToken = token.Trim();
        PasswordResetTokenExpiresAt = expiresAt;
    }

    public void ResetPassword(string newPasswordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);
        PasswordHash = newPasswordHash;
        PasswordResetToken = null;
        PasswordResetTokenExpiresAt = null;
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
}
