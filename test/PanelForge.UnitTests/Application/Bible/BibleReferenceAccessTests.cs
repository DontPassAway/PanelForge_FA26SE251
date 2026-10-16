using System.Text.Json;
using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Bible.Commands.UploadBibleReferenceImage;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntriesAtChapter;
using PanelForge.Application.Features.Bible.Queries.GetBibleReferenceImage;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>
/// FR-Artist-04, UC-07 bước 1, BR-15: Artist thấy đúng bản canon đã khoá ở chương đang vẽ, kèm ảnh của chính version đó.
/// </summary>
public class BibleReferenceAccessTests
{
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly BibleEntry _villain;

    public BibleReferenceAccessTests()
    {
        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Bible");
        var hero = bible.AddEntry(BibleEntryCategory.Character, "HERO", "Hero", "Grey coat");
        hero.AddRevision("v1", Snapshot("Grey coat", "/img/grey", 1), "h1", effectiveFromChapterNumber: 1, isInitialVersion: true)
            .Lock(Guid.NewGuid());
        // Bản chương 5 chưa khoá: màn hình Producer thấy, task của Artist thì chưa
        hero.AddRevision("v2", Snapshot("Red coat", "/img/red", 5), "h2", effectiveFromChapterNumber: 5);
        _villain = bible.AddEntry(BibleEntryCategory.Character, "VILLAIN", "Villain", "Black cape");
        _villain.AddRevision("v1", Snapshot("Black cape", null, 1), "h3", effectiveFromChapterNumber: 1, isInitialVersion: true);

        _db.Setup(d => d.SeriesBibles).Returns(DbSetMockHelper.CreateDbSetMock(new List<SeriesBible> { bible }));
    }

    private static string Snapshot(string description, string? image, int from)
        => JsonSerializer.Serialize(new { Name = "n", Description = description, ReferenceImageUrl = image, EffectiveFromChapterNumber = from });

    private async Task<IReadOnlyList<BibleEntryDto>> AtChapter(int chapter, bool canonOnly, IReadOnlyCollection<Guid>? ids = null)
        => (await new GetBibleEntriesAtChapterQueryHandler(_db.Object)
            .Handle(new GetBibleEntriesAtChapterQuery(_seriesId, chapter, canonOnly, ids), CancellationToken.None)).Value!;

    [Fact]
    public async Task CanonOnly_ShouldSkipUnlockedRevision_AndReturnImageOfLockedVersion()
    {
        var hero = (await AtChapter(6, canonOnly: true)).Single(e => e.Code == "HERO");

        hero.Description.Should().Be("Grey coat");
        hero.ReferenceImageUrl.Should().Be("/img/grey");
        hero.EffectiveRevision!.IsLocked.Should().BeTrue();
    }

    [Fact]
    public async Task Default_ShouldStillShowLatestDraftForProducerScreen()
    {
        var hero = (await AtChapter(6, canonOnly: false)).Single(e => e.Code == "HERO");

        hero.ReferenceImageUrl.Should().Be("/img/red");
    }

    [Fact]
    public async Task CanonOnly_ShouldDropCharacterWithoutAnyLockedVersion()
    {
        (await AtChapter(1, canonOnly: true)).Select(e => e.Code).Should().Equal("HERO");
    }

    [Fact]
    public async Task EntryIds_ShouldLimitResultToRequestedEntries()
    {
        (await AtChapter(1, canonOnly: false, [_villain.Id])).Select(e => e.Code).Should().Equal("VILLAIN");
    }

    [Fact]
    public async Task Upload_ShouldRejectNonImage()
    {
        var handler = new UploadBibleReferenceImageCommandHandler(Mock.Of<IAssetStorage>());

        var result = await handler.Handle(new UploadBibleReferenceImageCommand(_seriesId, "not an image"u8.ToArray()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Upload_ShouldStoreUnderSeriesAndReturnHashUrl()
    {
        var storage = new Mock<IAssetStorage>();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

        var result = await new UploadBibleReferenceImageCommandHandler(storage.Object)
            .Handle(new UploadBibleReferenceImageCommand(_seriesId, png), CancellationToken.None);

        result.Value!.Url.Should().Be($"/api/series/{_seriesId}/bible/assets/{result.Value.Sha256}");
        storage.Verify(s => s.SaveAsync($"bible/{_seriesId:N}/{result.Value.Sha256}", png, It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData("../../secrets")]
    [InlineData("abc")]
    public async Task GetImage_ShouldRejectNonHashKey_WithoutTouchingStorage(string key)
    {
        var storage = new Mock<IAssetStorage>(MockBehavior.Strict);

        var result = await new GetBibleReferenceImageQueryHandler(storage.Object)
            .Handle(new GetBibleReferenceImageQuery(_seriesId, key), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }
}
