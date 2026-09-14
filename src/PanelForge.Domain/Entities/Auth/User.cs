using PanelForge.Domain.Common;

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

    public ICollection<StudioWorkspace> OwnedWorkspaces { get; private set; } = [];
    public ICollection<WorkspaceMember> WorkspaceMemberships { get; private set; } = [];
    public ICollection<ExternalPreviewLink> CreatedPreviewLinks { get; private set; } = [];

    private User() { }

    public static User Create(
        string email,
        string fullName,
        string? passwordHash = null,
        string? firebaseUid = null,
        string? phoneNumber = null,
        string? avatarUrl = null)
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
            AvatarUrl = avatarUrl?.Trim()
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

    public void LinkFirebase(string firebaseUid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firebaseUid);
        FirebaseUid = firebaseUid.Trim();
    }

    public void Deactivate() => IsActive = false;
    public void Activate()   => IsActive = true;
}
