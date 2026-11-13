using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Ảnh trang của bản dịch (en/ja) do Publisher upload. Bản ngôn ngữ của chương phát hành cho Reader
/// khi mọi trang của chương đều có ảnh dịch. Bản gốc (vi) luôn dùng ảnh Studio sản xuất.
/// </summary>
public class PageTranslation : BaseEntity
{
    public Guid PageId { get; private set; }
    public Guid ChapterId { get; private set; }
    public string Language { get; private set; } = default!;
    public string ContentSha256 { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;

    public Page Page { get; private set; } = default!;

    private PageTranslation() { }

    public static string StorageKey(Guid pageId, string language, string sha256) =>
        $"page-translations/{pageId:N}/{language}/{sha256}";

    public static PageTranslation Create(Guid pageId, Guid chapterId, string language, string sha256, string contentType)
    {
        if (!ContentLanguages.Translations.Contains(language))
            throw new ArgumentException("Chỉ upload ảnh cho ngôn ngữ bản dịch (en, ja).", nameof(language));
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        return new PageTranslation
        {
            PageId = pageId,
            ChapterId = chapterId,
            Language = language,
            ContentSha256 = sha256,
            ContentType = contentType
        };
    }

    public void Replace(string sha256, string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ContentSha256 = sha256;
        ContentType = contentType;
        UpdatedAt = DateTime.UtcNow;
    }
}
