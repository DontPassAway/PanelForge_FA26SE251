using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;

namespace PanelForge.UnitTests.Common;

/// <summary>BR-33: tạo ấn bản ngôn ngữ đã được Publisher cấu hình Free/Trả phí cho test.</summary>
public static class TestEditions
{
    public static ChapterEdition Monetize(
        PanelForgeDbContext db,
        Guid chapterId,
        bool isPaid,
        decimal priceVnd = 0m,
        int freePreviewPages = 0,
        string language = ContentLanguages.Vietnamese,
        EditionStatus status = EditionStatus.Published)
    {
        var edition = ChapterEdition.Create(chapterId, language, status);
        edition.SetMonetization(isPaid, priceVnd, freePreviewPages, Guid.Empty);
        db.ChapterEditions.Add(edition);
        return edition;
    }
}
