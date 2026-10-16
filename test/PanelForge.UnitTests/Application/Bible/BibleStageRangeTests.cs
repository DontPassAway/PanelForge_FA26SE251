using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>
/// BR-15: mỗi giai đoạn là một revision có tên và phạm vi [from, to]. Ví dụ Goku: "Lúc nhỏ" chương 1–19, "Trưởng thành" từ chương 20.
/// </summary>
public class BibleStageRangeTests
{
    private static BibleEntry Goku()
    {
        var bible = SeriesBible.Create(Guid.NewGuid(), Guid.NewGuid(), "Bible");
        var goku = bible.AddEntry(BibleEntryCategory.Character, "CHR-GOKU", "Goku", "Saiyan");
        goku.AddRevision("Lúc nhỏ", "{}", "h1", effectiveFromChapterNumber: 1, isInitialVersion: true,
            stageName: "Lúc nhỏ", effectiveToChapterNumber: 19);
        goku.AddRevision("Trưởng thành", "{}", "h2", effectiveFromChapterNumber: 20, stageName: "Trưởng thành");
        return goku;
    }

    [Theory]
    [InlineData(1, "Lúc nhỏ")]
    [InlineData(19, "Lúc nhỏ")]
    [InlineData(20, "Trưởng thành")]
    [InlineData(300, "Trưởng thành")]
    public void EffectiveRevisionAt_PicksStageWhoseRangeCoversChapter(int chapter, string expected)
    {
        Goku().EffectiveRevisionAt(chapter)!.StageName.Should().Be(expected);
    }

    [Fact]
    public void EffectiveRevisionAt_AfterLastStageEnds_ReturnsNull()
    {
        var bible = SeriesBible.Create(Guid.NewGuid(), Guid.NewGuid(), "Bible");
        var mentor = bible.AddEntry(BibleEntryCategory.Character, "CHR-MENTOR", "Mentor", "Rời truyện ở chương 50");
        mentor.AddRevision("v1", "{}", "h", effectiveFromChapterNumber: 1, effectiveToChapterNumber: 50);

        mentor.EffectiveRevisionAt(50).Should().NotBeNull();
        mentor.EffectiveRevisionAt(51).Should().BeNull();
    }

    [Fact]
    public void EffectiveRevisionAt_OverlapWithoutTo_LaterStageWins()
    {
        var bible = SeriesBible.Create(Guid.NewGuid(), Guid.NewGuid(), "Bible");
        var e = bible.AddEntry(BibleEntryCategory.Character, "CHR-X", "X", "d");
        e.AddRevision("a", "{}", "h1", effectiveFromChapterNumber: 1, stageName: "A"); // không đặt to
        e.AddRevision("b", "{}", "h2", effectiveFromChapterNumber: 10, stageName: "B");

        e.EffectiveRevisionAt(12)!.StageName.Should().Be("B");
        e.EffectiveRevisionAt(9)!.StageName.Should().Be("A");
    }

    [Fact]
    public void Create_ToBeforeFrom_Throws()
    {
        var act = () => BibleEntryRevision.Create(Guid.NewGuid(), 1, "s", "{}", "h",
            effectiveFromChapterNumber: 10, effectiveToChapterNumber: 9);

        act.Should().Throw<ArgumentException>();
    }

    private static AddBibleEntryRevisionCommandHandler HandlerFor(BibleEntry entry, SeriesBible bible)
    {
        var db = new Mock<IPanelForgeDbContext>();
        db.Setup(d => d.SeriesBibles).Returns(DbSetMockHelper.CreateDbSetMock(new List<SeriesBible> { bible }));
        db.Setup(d => d.BibleEntries).Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntry> { entry }));
        db.Setup(d => d.BibleEntryRevisions).Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntryRevision>()));
        return new AddBibleEntryRevisionCommandHandler(db.Object);
    }

    [Fact]
    public async Task AddRevision_ToBeforeFrom_IsValidationError()
    {
        var bible = SeriesBible.Create(Guid.NewGuid(), Guid.NewGuid(), "Bible");
        var e = bible.AddEntry(BibleEntryCategory.Character, "CHR-Y", "Y", "d");
        e.AddRevision("v1", "{}", "h", isInitialVersion: true);

        var result = await HandlerFor(e, bible).Handle(new AddBibleEntryRevisionCommand(bible.SeriesId, e.Id, Guid.NewGuid(), "s", "Y", "d",
            EffectiveFromChapterNumber: 20, EffectiveToChapterNumber: 5), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Supersede_KeepsStageNameAndRangeOfReplacedRevision()
    {
        var bible = SeriesBible.Create(Guid.NewGuid(), Guid.NewGuid(), "Bible");
        var e = bible.AddEntry(BibleEntryCategory.Character, "CHR-GOKU", "Goku", "d");
        var young = e.AddRevision("v1", "{}", "h", isInitialVersion: true, stageName: "Lúc nhỏ", effectiveToChapterNumber: 19);
        young.Lock(Guid.NewGuid());

        var result = await HandlerFor(e, bible).Handle(new AddBibleEntryRevisionCommand(bible.SeriesId, e.Id, Guid.NewGuid(), "Sửa màu áo", "Goku", "d2",
            SupersedesRevisionId: young.Id, SupersedeReason: "Sửa màu áo"), CancellationToken.None);

        result.Value!.StageName.Should().Be("Lúc nhỏ");
        result.Value.EffectiveToChapterNumber.Should().Be(19);
        result.Value.IsLocked.Should().BeTrue();
    }
}
