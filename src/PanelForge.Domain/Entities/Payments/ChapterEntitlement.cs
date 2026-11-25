using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Domain.Entities.Payments;

/// <summary>
/// Quyền đọc chương đã mở khóa của độc giả (đã mua vĩnh viễn hoặc qua giao dịch).
/// </summary>
public class ChapterEntitlement : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid? TransactionId { get; private set; }
    public decimal PricePaidVnd { get; private set; }

    /// <summary>Ngôn ngữ bản phát hành đã mua (mua theo từng ngôn ngữ).</summary>
    public string Language { get; private set; } = Enums.ContentLanguages.Original;
    public DateTime UnlockedAt { get; private set; } = DateTime.UtcNow;

    public User User { get; private set; } = default!;
    public Chapter Chapter { get; private set; } = default!;
    public Series Series { get; private set; } = default!;
    public StudioWorkspace Workspace { get; private set; } = default!;
    public PaymentTransaction? Transaction { get; private set; }

    private ChapterEntitlement() { }

    public static ChapterEntitlement Create(
        Guid userId,
        Guid chapterId,
        Guid seriesId,
        Guid workspaceId,
        decimal pricePaidVnd,
        Guid? transactionId = null,
        DateTime? unlockedAt = null,
        string? language = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pricePaidVnd);

        return new ChapterEntitlement
        {
            UserId = userId,
            ChapterId = chapterId,
            SeriesId = seriesId,
            WorkspaceId = workspaceId,
            PricePaidVnd = pricePaidVnd,
            TransactionId = transactionId,
            UnlockedAt = unlockedAt ?? DateTime.UtcNow,
            Language = Enums.ContentLanguages.Normalize(language) ?? Enums.ContentLanguages.Original
        };
    }
}

