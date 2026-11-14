using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Bible;

/// <summary>
/// Bảng chỉ mục tên chuẩn hoá chống trùng tên/bí danh và chống race khi tự tạo mục (BIB-09, 9A).
/// Khóa chính là (series_bible_id, normalized_name).
/// </summary>
public class BibleEntryName
{
    public Guid SeriesBibleId { get; private set; }
    public string NormalizedName { get; private set; } = default!;
    public Guid BibleEntryId { get; private set; }
    public bool IsPrimary { get; private set; }

    public SeriesBible SeriesBible { get; private set; } = default!;
    public BibleEntry BibleEntry { get; private set; } = default!;

    private BibleEntryName() { }

    public static BibleEntryName Create(
        Guid seriesBibleId,
        string rawName,
        Guid bibleEntryId,
        bool isPrimary)
    {
        var normalized = BibleNameNormalizer.Normalize(rawName);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Tên hoặc bí danh sau khi chuẩn hoá không được để trống.", nameof(rawName));

        if (normalized.Length > 200)
            throw new ArgumentException("Tên chuẩn hoá tối đa 200 ký tự.", nameof(rawName));

        return new BibleEntryName
        {
            SeriesBibleId = seriesBibleId,
            NormalizedName = normalized,
            BibleEntryId = bibleEntryId,
            IsPrimary = isPrimary
        };
    }
}
