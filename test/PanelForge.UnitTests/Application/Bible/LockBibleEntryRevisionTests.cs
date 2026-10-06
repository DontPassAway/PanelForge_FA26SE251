using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.LockBibleEntryRevision;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>V1.2 (CF1): Producer khoá revision của character sheet; bản khoá chỉ được thay bằng bản mới ở chương khác, không ghi đè tại chỗ.</summary>
public class LockBibleEntryRevisionTests
{
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly BibleEntry _hero;
    private readonly BibleEntryRevision _heroV1;
    private readonly BibleEntry _location;
    private readonly BibleEntryRevision _locationV1;

    public LockBibleEntryRevisionTests()
    {
        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Bible");
        _hero = bible.AddEntry(BibleEntryCategory.Character, "HERO", "Hero", "Anh hùng");
        _heroV1 = _hero.AddRevision("v1", "{}", "hash-1", effectiveFromChapterNumber: 1, isInitialVersion: true);
        _location = bible.AddEntry(BibleEntryCategory.Location, "SHOP", "Tiệm", "Tiệm đồng hồ");
        _locationV1 = _location.AddRevision("v1", "{}", "hash-2", isInitialVersion: true);

        _db.Setup(d => d.SeriesBibles).Returns(DbSetMockHelper.CreateDbSetMock(new List<SeriesBible> { bible }));
        _db.Setup(d => d.BibleEntries).Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntry> { _hero, _location }));
        _db.Setup(d => d.BibleEntryRevisions).Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntryRevision> { _heroV1, _locationV1 }));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private Task<Result<PanelForge.Application.Features.Bible.Models.BibleEntryRevisionDto>> Lock(
        BibleEntry entry, BibleEntryRevision revision, Guid? seriesId = null) =>
        new LockBibleEntryRevisionCommandHandler(_db.Object).Handle(
            new LockBibleEntryRevisionCommand(seriesId ?? _seriesId, entry.Id, revision.Id, _producerId), CancellationToken.None);

    private Task<Result<PanelForge.Application.Features.Bible.Models.BibleEntryRevisionDto>> AddRevision(int effectiveFrom) =>
        new AddBibleEntryRevisionCommandHandler(_db.Object).Handle(
            new AddBibleEntryRevisionCommand(_seriesId, _hero.Id, _producerId, "v mới", "Hero", "Anh hùng (bản mới)",
                EffectiveFromChapterNumber: effectiveFrom), CancellationToken.None);

    [Fact]
    public async Task Lock_CharacterRevision_MarksItLockedAndRecordsWho()
    {
        var result = await Lock(_hero, _heroV1);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.IsLocked.Should().BeTrue();
        result.Value.LockedById.Should().Be(_producerId);
        result.Value.LockedAt.Should().NotBeNull();
        _heroV1.IsLocked.Should().BeTrue();
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lock_NonCharacterEntry_IsRejected_AndNotSaved()
    {
        var result = await Lock(_location, _locationV1);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _locationV1.IsLocked.Should().BeFalse();
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Lock_AlreadyLocked_ReturnsConflict_AndKeepsOriginalLocker()
    {
        await Lock(_hero, _heroV1);
        var firstLockedAt = _heroV1.LockedAt;

        var second = await new LockBibleEntryRevisionCommandHandler(_db.Object).Handle(
            new LockBibleEntryRevisionCommand(_seriesId, _hero.Id, _heroV1.Id, Guid.NewGuid()), CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _heroV1.LockedById.Should().Be(_producerId);
        _heroV1.LockedAt.Should().Be(firstLockedAt);
    }

    [Fact]
    public async Task Lock_WhenEntryBelongsToAnotherSeries_ReturnsNotFound()
    {
        var result = await Lock(_hero, _heroV1, seriesId: Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        _heroV1.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task AddRevision_StartingAtTheSameChapterAsALockedRevision_ReturnsConflict_AndNotSaved()
    {
        await Lock(_hero, _heroV1);
        _db.Invocations.Clear();

        var result = await AddRevision(effectiveFrom: 1);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _hero.Revisions.Should().HaveCount(1);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddRevision_StartingAtAnotherChapter_ReplacesLockedOneFromThatChapter_WithoutTouchingIt()
    {
        await Lock(_hero, _heroV1);

        var result = await AddRevision(effectiveFrom: 5);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.IsLocked.Should().BeFalse();
        _heroV1.IsLocked.Should().BeTrue();
        _heroV1.SnapshotJson.Should().Be("{}");
        _hero.Revisions.Should().HaveCount(2);
    }
}
