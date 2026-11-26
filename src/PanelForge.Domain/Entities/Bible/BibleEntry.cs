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
    public string Name { get; private set; } = default!;
    public string NormalizedName { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string DetailsJson { get; private set; } = "{}";
    public string? ReferenceImageUrl { get; private set; }
    /// <summary>V2.5: Đánh dấu thực thể được tạo tạm thời từ kịch bản (Write First, Bible Later).</summary>
    public bool IsStub { get; private set; } = false;
    /// <summary>BIB-11 & 7A: Cờ Quần chúng; mục quần chúng không bắt buộc khoá Canon ở Gate 1.</summary>
    public bool IsCrowd { get; private set; } = false;

    public bool HasLockedRevision => Revisions.Any(r => r.IsLocked);

    public SeriesBible SeriesBible { get; private set; } = default!;
    public ICollection<BibleEntryRevision> Revisions { get; private set; } = [];

    private BibleEntry() { }

    public static BibleEntry Create(
        Guid seriesBibleId,
        BibleEntryCategory category,
        string code,
        string name,
        string description,
        string? detailsJson = null,
        string? referenceImageUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new BibleEntry
        {
            SeriesBibleId = seriesBibleId,
            Category = category,
            Name = name.Trim(),
            NormalizedName = BibleNameNormalizer.Normalize(name),
            Description = description.Trim(),
            DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim(),
            ReferenceImageUrl = referenceImageUrl?.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static BibleEntry Create(
        Guid seriesBibleId,
        BibleEntryCategory category,
        string name,
        string description)
    {
        return Create(seriesBibleId, category, string.Empty, name, description);
    }

    /// <summary>
    /// V2.5: Tạo nhanh thực thể tạm (Stub) khi Writer đề cập trong kịch bản.
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

        return new BibleEntry
        {
            SeriesBibleId = seriesBibleId,
            Category = category,
            Name = trimmedName,
            NormalizedName = BibleNameNormalizer.Normalize(trimmedName),
            Description = description?.Trim() ?? string.Empty,
            DetailsJson = "{}",
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
        string? detailsJson,
        string? referenceImageUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Description = description.Trim();
        DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim();
        ReferenceImageUrl = referenceImageUrl?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsStub(bool isStub = true)
    {
        IsStub = isStub;
        UpdatedAt = DateTime.UtcNow;
    }

    public void PromoteFromStub(
        string name,
        string description,
        string? detailsJson = null,
        string? referenceImageUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Name = name.Trim();
        NormalizedName = BibleNameNormalizer.Normalize(name);
        Description = description.Trim();
        DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim();
        ReferenceImageUrl = referenceImageUrl?.Trim();
        IsStub = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RenameBeforeFirstLock(string newName)
    {
        if (HasLockedRevision)
            throw new InvalidOperationException("Không thể đổi tên mục Bible sau khi đã có phiên bản khoá Canon.");
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        Name = newName.Trim();
        NormalizedName = BibleNameNormalizer.Normalize(Name);
        UpdatedAt = DateTime.UtcNow;
    }

    public bool MatchesNormalizedName(string normalizedKey)
    {
        if (string.IsNullOrWhiteSpace(normalizedKey)) return false;
        return string.Equals(NormalizedName, normalizedKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Danh mục có thiết kế hình (Nhân vật, Địa điểm, Đạo cụ): Artist nộp Concept → Editor duyệt → Producer khoá Canon,
    /// và AI chỉ dùng bản đã khoá. Thuật ngữ/Quy tắc/Sự kiện chỉ là văn bản, có hiệu lực ngay không cần khoá.
    /// </summary>
    public bool IsVisualDesign
        => Category is BibleEntryCategory.Character or BibleEntryCategory.Location or BibleEntryCategory.Prop;

    /// <summary>Bản mới nhất của từng biến thể / giai đoạn.</summary>
    public IEnumerable<BibleEntryRevision> LatestRevisionPerStage()
        => Revisions
            .GroupBy(r => BibleNameNormalizer.Normalize(r.StageName))
            .Where(g => !string.IsNullOrEmpty(g.Key))
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
        var effectiveStage = string.IsNullOrWhiteSpace(stageName) ? "Mặc định" : stageName.Trim();
        var normalizedStage = BibleNameNormalizer.Normalize(effectiveStage);

        var existingVersions = Revisions
            .Where(r => string.Equals(BibleNameNormalizer.Normalize(r.StageName), normalizedStage, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.VersionNumber)
            .ToList();
        var nextVersion = existingVersions.Count > 0 ? existingVersions.Max() + 1 : 1;

        var revision = BibleEntryRevision.Create(
            Id,
            nextVersion,
            summary,
            snapshotJson,
            contentHash,
            authorId,
            associatedChapterId,
            isInitialVersion,
            effectiveStage,
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
    /// Superseded của biến thể "Mặc định"; nếu chưa có thì tìm CanonLocked mới nhất của biến thể bất kỳ;
    /// nếu chưa có Canon nào thì dùng version mới nhất. Tuyệt đối không để bản nháp Draft ghi đè khi đã có bản Canon.
    /// </summary>
    public BibleEntryRevision? GetDisplayRevision()
    {
        if (Revisions.Count == 0) return null;

        var defaultStageName = GetDefaultVariantStageName() ?? "Mặc định";
        var normDefault = BibleNameNormalizer.Normalize(defaultStageName);

        // 1. Ưu tiên bản CanonLocked mới nhất chưa bị Superseded của biến thể mặc định
        var defaultCanon = Revisions
            .Where(r => string.Equals(BibleNameNormalizer.Normalize(r.StageName), normDefault, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.ReviewStatus == BibleRevisionStatus.CanonLocked && !IsRevisionSuperseded(r))
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefault();

        if (defaultCanon != null) return defaultCanon;

        // 2. Nếu biến thể mặc định chưa có Canon, tìm bản CanonLocked mới nhất của biến thể khác
        var anyCanon = Revisions
            .Where(r => r.ReviewStatus == BibleRevisionStatus.CanonLocked && !IsRevisionSuperseded(r))
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefault();

        if (anyCanon != null) return anyCanon;

        // 3. Fallback khi hoàn toàn chưa có bản Canon nào: lấy bản mới nhất của biến thể mặc định, hoặc bản mới nhất bất kỳ
        var defaultLatest = Revisions
            .Where(r => string.Equals(BibleNameNormalizer.Normalize(r.StageName), normDefault, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefault();

        return defaultLatest ?? GetLatestRevision();
    }

    public BibleEntryRevision? GetCurrentRevision() => GetDisplayRevision();

    /// <summary>Revision mới nhất theo VersionNumber, bất kể trạng thái duyệt.</summary>
    public BibleEntryRevision? GetLatestRevision()
        => Revisions.OrderByDescending(r => r.VersionNumber).FirstOrDefault();

    /// <summary>
    /// Tên biến thể mặc định của mục: ưu tiên "Mặc định" hoặc stageName của bản tạo sớm nhất.
    /// </summary>
    public string? GetDefaultVariantStageName()
    {
        var defaultNamed = Revisions.FirstOrDefault(r =>
            string.Equals(BibleNameNormalizer.Normalize(r.StageName), "mac dinh", StringComparison.OrdinalIgnoreCase));
        if (defaultNamed != null) return defaultNamed.StageName;

        return Revisions.OrderBy(r => r.CreatedAt).FirstOrDefault()?.StageName ?? "Mặc định";
    }

    /// <summary>
    /// Lấy bản của biến thể mặc định (Q23, Q24):
    /// Ưu tiên bản CanonLocked mới nhất chưa bị thay thế; nếu chưa có bản khoá thì lấy bản mới nhất
    /// (nếu canonOnly = true và IsVisualDesign thì trả về null).
    /// </summary>
    public BibleEntryRevision? GetDefaultVariantRevision(bool canonOnly = false)
    {
        if (Revisions.Count == 0) return null;

        var defaultStageName = GetDefaultVariantStageName() ?? "Mặc định";
        var normDefault = BibleNameNormalizer.Normalize(defaultStageName);

        var defaultVariantRevisions = Revisions
            .Where(r => string.Equals(BibleNameNormalizer.Normalize(r.StageName), normDefault, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (defaultVariantRevisions.Count == 0)
        {
            var firstRev = Revisions.OrderBy(r => r.CreatedAt).FirstOrDefault();
            if (canonOnly && IsVisualDesign)
                return (firstRev != null && firstRev.IsLocked && !IsRevisionSuperseded(firstRev)) ? firstRev : null;
            return firstRev;
        }

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
    /// Dừng lại nếu bản thay thế mới nhất vẫn đang ở Draft/ConceptSubmitted/ReworkRequired.
    /// </summary>
    public BibleEntryRevision ResolveLatestLockedReplacement(Guid initialRevisionId)
    {
        var current = Revisions.FirstOrDefault(r => r.Id == initialRevisionId);
        if (current == null)
            return GetDefaultVariantRevision(canonOnly: true) ?? GetDefaultVariantRevision(canonOnly: false)
                ?? throw new InvalidOperationException($"Revision {initialRevisionId} không tồn tại trong mục Bible {Id}.");

        var currentId = current.Id;
        var visited = new HashSet<Guid> { currentId };

        while (true)
        {
            var nextLocked = Revisions
                .Where(r => r.SupersedesRevisionId == currentId && r.IsLocked && !visited.Contains(r.Id))
                .OrderByDescending(r => r.VersionNumber)
                .FirstOrDefault();

            if (nextLocked == null) break;
            visited.Add(nextLocked.Id);
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
    /// Kiểm tra xem mục đã có biến thể mang nhãn này (so sánh chuẩn hoá không phân biệt hoa thường/dấu).
    /// </summary>
    public bool HasVariant(string variantLabel)
    {
        if (string.IsNullOrWhiteSpace(variantLabel)) return false;
        var normalized = BibleNameNormalizer.Normalize(variantLabel);
        if (string.IsNullOrEmpty(normalized)) return false;

        return Revisions
            .Any(r => !r.IsDeleted && !IsRevisionSuperseded(r) &&
                      string.Equals(BibleNameNormalizer.Normalize(r.StageName), normalized, StringComparison.OrdinalIgnoreCase));
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
