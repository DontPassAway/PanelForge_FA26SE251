using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Bible;

/// <summary>
/// Đại diện cho một thực thể cấu trúc trong Series Bible (Nhân vật, Địa điểm, Đạo cụ, Thuật ngữ, Quy tắc phong cách, Sự kiện cốt truyện).
/// </summary>
public class BibleEntry : BaseEntity
{
    /// <summary>Concurrency token ánh xạ cột hệ thống <c>xmin</c> của PostgreSQL (BR-11): hai lần ghi chồng nhau → 409.</summary>
    public uint RowVersion { get; private set; }

    public Guid SeriesBibleId { get; private set; }
    public BibleEntryCategory Category { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string NormalizedName { get; private set; } = default!;
    public string? Subtitle { get; private set; }
    public string Description { get; private set; } = default!;
    public string DetailsJson { get; private set; } = "{}";
    public string? ReferenceImageUrl { get; private set; }
    public BiblePriority Priority { get; private set; } = BiblePriority.Standard;
    public bool StrictCheck { get; private set; } = true;
    public bool IsActive { get; private set; } = true;
    /// <summary>V2.5: Đánh dấu thực thể được tạo tạm thời từ cú pháp @-mention trong kịch bản (Write First, Bible Later).</summary>
    public bool IsStub { get; private set; } = false;
    /// <summary>BIB-09 & 9A: Danh sách bí danh của mục (tối đa 20 bí danh, mỗi bí danh <= 100 ký tự).</summary>
    /// <summary>BIB-11 & 7A: Cờ Quần chúng; mục quần chúng không bắt buộc khoá Canon ở Gate 1.</summary>
    public bool IsCrowd { get; private set; } = false;

    public SeriesBible SeriesBible { get; private set; } = default!;
    public ICollection<BibleEntryRevision> Revisions { get; private set; } = [];

    private BibleEntry() { }

    public static BibleEntry Create(
        Guid seriesBibleId,
        BibleEntryCategory category,
        string code,
        string name,
        string description,
        string? subtitle = null,
        string? detailsJson = null,
        string? referenceImageUrl = null,
        BiblePriority priority = BiblePriority.Standard,
        bool strictCheck = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new BibleEntry
        {
            SeriesBibleId = seriesBibleId,
            Category = category,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            NormalizedName = BibleNameNormalizer.Normalize(name),
            Subtitle = subtitle?.Trim(),
            Description = description.Trim(),
            DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim(),
            ReferenceImageUrl = referenceImageUrl?.Trim(),
            Priority = priority,
            StrictCheck = strictCheck,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// V2.5: Tạo nhanh thực thể tạm (Stub) khi Writer gõ @-mention trong kịch bản.
    /// Cho phép Writer viết tự do (Write First, Bible Later).
    /// </summary>
    public static BibleEntry CreateStub(
        Guid seriesBibleId,
        BibleEntryCategory category,
        string name,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = name.Trim();
        var code = "STUB_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        return new BibleEntry
        {
            SeriesBibleId = seriesBibleId,
            Category = category,
            Code = code,
            Name = trimmedName,
            NormalizedName = BibleNameNormalizer.Normalize(trimmedName),
            Description = description?.Trim() ?? string.Empty,
            DetailsJson = "{}",
            Priority = BiblePriority.Standard,
            StrictCheck = false,
            IsActive = true,
            IsStub = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void SetCrowd(bool isCrowd)
    {
        IsCrowd = isCrowd;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDetails(
        string description,
        string? subtitle,
        string? detailsJson,
        string? referenceImageUrl,
        BiblePriority priority,
        bool strictCheck)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Description = description.Trim();
        Subtitle = subtitle?.Trim();
        DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim();
        ReferenceImageUrl = referenceImageUrl?.Trim();
        Priority = priority;
        StrictCheck = strictCheck;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsStub(bool isStub = true)
    {
        IsStub = isStub;
        UpdatedAt = DateTime.UtcNow;
    }

    public void PromoteFromStub(
        string code,
        string name,
        string description,
        string? subtitle = null,
        string? detailsJson = null,
        string? referenceImageUrl = null,
        BiblePriority priority = BiblePriority.Standard,
        bool strictCheck = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        NormalizedName = BibleNameNormalizer.Normalize(name);
        Description = description.Trim();
        Subtitle = subtitle?.Trim();
        DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim();
        ReferenceImageUrl = referenceImageUrl?.Trim();
        Priority = priority;
        StrictCheck = strictCheck;
        IsStub = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActiveStatus(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Danh mục có thiết kế hình (Nhân vật, Địa điểm, Đạo cụ): Artist nộp Concept → Editor duyệt → Producer khoá Canon,
    /// và AI chỉ dùng bản đã khoá. Thuật ngữ/Quy tắc/Sự kiện chỉ là văn bản, có hiệu lực ngay không cần khoá.
    /// </summary>
    public bool IsVisualDesign
        => Category is BibleEntryCategory.Character or BibleEntryCategory.Location or BibleEntryCategory.Prop;

    /// <summary>
    /// <summary>Bản mới nhất của từng biến thể / giai đoạn.</summary>
    public IEnumerable<BibleEntryRevision> LatestRevisionPerStage()
        => Revisions
            .GroupBy(r => r.StageName ?? string.Empty)
            .Select(g => g.MaxBy(r => r.VersionNumber)!);

    public BibleEntryRevision AddRevision(
        string summary,
        string snapshotJson,
        string contentHash,
        Guid? authorId = null,
        Guid? associatedChapterId = null,
        bool isInitialVersion = false,
        string? stageName = null,
        Guid? artistUserId = null,
        BibleRevisionStatus reviewStatus = BibleRevisionStatus.Draft,
        string? reworkFeedback = null,
        Guid? supersedesRevisionId = null)
    {
        var nextVersion = (Revisions.Count > 0) ? Revisions.Max(r => r.VersionNumber) + 1 : 1;
        var revision = BibleEntryRevision.Create(
            Id,
            nextVersion,
            summary,
            snapshotJson,
            contentHash,
            authorId,
            associatedChapterId,
            isInitialVersion,
            stageName,
            artistUserId,
            reviewStatus,
            reworkFeedback,
            supersedesRevisionId);

        Revisions.Add(revision);
        UpdatedAt = DateTime.UtcNow;
        return revision;
    }

    public void RemoveRevision(BibleEntryRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        if (revision.IsLocked)
            throw new InvalidOperationException("Không thể xóa revision đã được khóa Canon.");

        if (revision.IsInitialVersion)
            throw new InvalidOperationException("Không thể xóa phiên bản gốc ban đầu (Initial Version).");

        if (revision.HasReviewHistory)
            throw new InvalidOperationException("Không thể xóa phiên bản đã có lịch sử nộp Concept/kiểm duyệt (append-only).");

        Revisions.Remove(revision);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Revision dùng để hiển thị ở cấp mục Bible: ưu tiên CanonLocked mới nhất chưa bị
    /// Superseded của biến thể mặc định; khi chưa có Canon thì dùng version mới nhất.
    /// </summary>
    public BibleEntryRevision? GetDisplayRevision()
    {
        if (Revisions.Count == 0) return null;

        var defaultStageName = GetDefaultVariantStageName();
        var canon = Revisions
            .Where(r => string.Equals(r.StageName, defaultStageName, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.ReviewStatus == BibleRevisionStatus.CanonLocked && !IsRevisionSuperseded(r))
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefault();

        return canon ?? GetLatestRevision();
    }

    /// <summary>Revision mới nhất theo VersionNumber, bất kể trạng thái duyệt.</summary>
    public BibleEntryRevision? GetLatestRevision()
        => Revisions.OrderByDescending(r => r.VersionNumber).FirstOrDefault();

    /// <summary>
    /// Tên biến thể mặc định của mục = StageName của bản đầu tiên (VersionNumber nhỏ nhất).
    /// </summary>
    public string? GetDefaultVariantStageName()
        => Revisions.OrderBy(r => r.VersionNumber).FirstOrDefault()?.StageName;

    /// <summary>
    /// Lấy bản của biến thể mặc định (Q23, Q24):
    /// Ưu tiên bản CanonLocked mới nhất chưa bị thay thế; nếu chưa có bản khoá thì lấy bản mới nhất
    /// (nếu canonOnly = true và IsVisualDesign thì trả về null).
    /// </summary>
    public BibleEntryRevision? GetDefaultVariantRevision(bool canonOnly = false)
    {
        if (Revisions.Count == 0) return null;

        var firstRev = Revisions.OrderBy(r => r.VersionNumber).FirstOrDefault();
        var defaultStageName = firstRev?.StageName;

        var defaultVariantRevisions = Revisions
            .Where(r => string.Equals(r.StageName, defaultStageName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (defaultVariantRevisions.Count == 0) return firstRev;

        if (canonOnly && IsVisualDesign)
        {
            return defaultVariantRevisions
                .Where(r => r.IsLocked && !IsRevisionSuperseded(r))
                .OrderByDescending(r => r.VersionNumber)
                .FirstOrDefault();
        }

        return defaultVariantRevisions
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefault();
    }

    /// <summary>
    /// Đi theo chuỗi supersedes_revision_id tới bản đã khoá Canon mới nhất (Q23, Q24).
    /// </summary>
    public BibleEntryRevision ResolveLatestLockedReplacement(Guid initialRevisionId)
    {
        var currentId = initialRevisionId;
        while (true)
        {
            var nextLocked = Revisions
                .Where(r => r.SupersedesRevisionId == currentId && r.IsLocked)
                .OrderByDescending(r => r.VersionNumber)
                .FirstOrDefault();

            if (nextLocked == null) break;
            currentId = nextLocked.Id;
        }

        return Revisions.First(r => r.Id == currentId);
    }

    /// <summary>
    /// Kiểm tra revision đã bị supersede hay chưa (có sự kiện Superseded hoặc có bản thay thế đã khoá).
    /// </summary>
    public bool IsRevisionSuperseded(BibleEntryRevision rev)
    {
        return rev.Events.Any(e => e.Type == BibleRevisionEventType.Superseded)
            || Revisions.Any(other => other.SupersedesRevisionId == rev.Id && other.IsLocked);
    }

    /// <summary>
    /// Kiểm tra xem mục đã có biến thể mang nhãn này (so sánh sau khi chuẩn hoá BibleNameNormalizer)
    /// và đang hoạt động (có ít nhất một revision chưa bị supersede) hay không.
    /// </summary>
    public bool HasActiveVariant(string variantLabel)
    {
        if (string.IsNullOrWhiteSpace(variantLabel)) return false;
        var normalized = BibleNameNormalizer.Normalize(variantLabel);
        if (string.IsNullOrEmpty(normalized)) return false;

        return Revisions
            .GroupBy(r => BibleNameNormalizer.Normalize(r.StageName))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Where(g => g.Any(r => !IsRevisionSuperseded(r)))
            .Any(g => g.Key == normalized);
    }
}
