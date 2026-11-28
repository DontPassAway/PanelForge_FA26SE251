using FluentAssertions;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Chapters;

/// <summary>
/// REL-08: ấn bản dịch dùng chung artwork với bản gốc. Ghim snapshot khi dựng ảnh trang phải theo đúng ngôn ngữ, nếu không
/// xuất file / xem thử bản gốc lấy version lớp chữ của snapshot ấn bản dịch (duyệt sau, chứa chữ sửa sau lần duyệt gốc).
/// </summary>
public class PageImagePinLanguageTests
{
    private static readonly Guid ChapterId = Guid.NewGuid();
    private static readonly Guid PageId = Guid.NewGuid();
    private static readonly Guid Artwork = Guid.NewGuid();

    private static ChapterSnapshot Snapshot(int number, long canvasVersion, string language)
    {
        var manifest = AiJson.Serialize(new SnapshotManifest(ChapterId, 1,
            [new SnapshotPage(PageId, 1, canvasVersion, Artwork, "sha", WidthPx: 1200, HeightPx: 1800)], null, [], []));
        return ChapterSnapshot.Create(ChapterId, number, 1, Guid.NewGuid(), manifest, "hash", language);
    }

    private static IPanelForgeDbContext Db(params ChapterSnapshot[] snapshots)
    {
        var db = new Mock<IPanelForgeDbContext>();
        db.Setup(d => d.ChapterSnapshots).Returns(DbSetMockHelper.CreateDbSetMock(snapshots.ToList()));
        return db.Object;
    }

    [Fact]
    public async Task FindPin_IgnoresNewerTranslationSnapshot_WithSameArtwork()
    {
        var db = Db(Snapshot(1, canvasVersion: 5, ContentLanguages.Original),
            Snapshot(2, canvasVersion: 9, ContentLanguages.English));

        var pin = await PageImageBuilder.FindPinAsync(db, ChapterId, PageId, new HashSet<Guid> { Artwork }, CancellationToken.None);

        pin!.CanvasStreamVersion.Should().Be(5);
    }

    [Fact]
    public async Task FindPin_OnlyTranslationSnapshot_ReturnsNull()
    {
        var db = Db(Snapshot(1, canvasVersion: 9, ContentLanguages.English));

        var pin = await PageImageBuilder.FindPinAsync(db, ChapterId, PageId, new HashSet<Guid> { Artwork }, CancellationToken.None);

        pin.Should().BeNull();
    }
}
