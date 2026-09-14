using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Auth;

public class ExternalPreviewLink : BaseEntity
{
    public Guid ChapterId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public string WatermarkText { get; private set; } = default!;
    public DateTime ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public User CreatedByUser { get; private set; } = default!;

    private ExternalPreviewLink() { }

    public static ExternalPreviewLink Create(
        Guid chapterId,
        string tokenHash,
        string watermarkText,
        DateTime expiresAt,
        Guid createdByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(watermarkText);

        if (expiresAt <= DateTime.UtcNow)
            throw new ArgumentException("ExpiresAt phải ở tương lai.", nameof(expiresAt));

        return new ExternalPreviewLink
        {
            ChapterId = chapterId,
            TokenHash = tokenHash,
            WatermarkText = watermarkText.Trim(),
            ExpiresAt = expiresAt,
            IsRevoked = false,
            CreatedByUserId = createdByUserId
        };
    }

    public void Revoke() => IsRevoked = true;

    public bool IsValid(DateTime utcNow) => !IsRevoked && utcNow < ExpiresAt;
}
