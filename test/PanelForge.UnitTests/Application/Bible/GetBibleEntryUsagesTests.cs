using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntryUsages;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>"Xuất hiện trong kịch bản" của workbench Bible: cùng cách đếm và điều kiện Canon với Gate 1 (BR-21, BR-15).</summary>
public class GetBibleEntryUsagesTests
{
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly BibleEntry _hero;
    private readonly BibleEntry _term;
    private readonly Chapter _ch1;
    private readonly Chapter _ch3;

    public GetBibleEntryUsagesTests()
    {
        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Bible");
        _hero = bible.AddEntry(BibleEntryCategory.Character, "HERO", "Hero", "Anh hùng");
        // Chỉ khoá giai đoạn từ chương 1 đến 2; chương 3 trở đi chưa có bản Canon
        var heroV1 = _hero.AddRevision("v1", "{}", "h1", effectiveFromChapterNumber: 1, effectiveToChapterNumber: 2,
            isInitialVersion: true, reviewStatus: BibleRevisionStatus.WriterEndorsed);
        heroV1.Lock(Guid.NewGuid());
        _term = bible.AddEntry(BibleEntryCategory.Terminology, "AURA", "Aura", "Năng lượng");
        _term.AddRevision("v1", "{}", "h2", isInitialVersion: true);

        _ch1 = Chapter.Create(_seriesId, 1, "Mở đầu");
        _ch3 = Chapter.Create(_seriesId, 3, "Biến cố");
        var otherSeriesChapter = Chapter.Create(Guid.NewGuid(), 1, "Series khác");
        var s1 = Scene.Create(_ch1.Id, 1);
        var s3 = Scene.Create(_ch3.Id, 1);
        var sOther = Scene.Create(otherSeriesChapter.Id, 1);

        var speakerLine = ScriptLine.Create(s1.Id, 1, "Xin chào", speakerCharacterId: _hero.Id);
        var taggedLine = ScriptLine.Create(s1.Id, 2, stageDirection: "@Hero bước vào");
        var ch3Line = ScriptLine.Create(s3.Id, 1, stageDirection: "@Hero nhìn xa");
        var otherLine = ScriptLine.Create(sOther.Id, 1, "Series khác", speakerCharacterId: _hero.Id);
        var unrelatedLine = ScriptLine.Create(s1.Id, 3, "Không liên quan");

        _db.Setup(d => d.SeriesBibles).Returns(DbSetMockHelper.CreateDbSetMock(new List<SeriesBible> { bible }));
        _db.Setup(d => d.BibleEntries).Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntry> { _hero, _term }));
        _db.Setup(d => d.ScriptLineBibleLinks).Returns(DbSetMockHelper.CreateDbSetMock(new List<ScriptLineBibleLink>
        {
            ScriptLineBibleLink.Create(taggedLine.Id, _hero.Id, "@Hero"),
            ScriptLineBibleLink.Create(ch3Line.Id, _hero.Id, "@Hero"),
        }));
        _db.Setup(d => d.ScriptLines).Returns(DbSetMockHelper.CreateDbSetMock(
            new List<ScriptLine> { speakerLine, taggedLine, ch3Line, otherLine, unrelatedLine }));
        _db.Setup(d => d.Scenes).Returns(DbSetMockHelper.CreateDbSetMock(new List<Scene> { s1, s3, sOther }));
        _db.Setup(d => d.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter> { _ch1, _ch3, otherSeriesChapter }));
    }

    private Task<PanelForge.Application.Common.Result<IReadOnlyList<PanelForge.Application.Features.Bible.Models.BibleEntryUsageDto>>> Run(Guid entryId, Guid? seriesId = null) =>
        new GetBibleEntryUsagesQueryHandler(_db.Object).Handle(new GetBibleEntryUsagesQuery(seriesId ?? _seriesId, entryId), CancellationToken.None);

    [Fact]
    public async Task CountsSpeakerAndTaggedLinesPerChapter_AndFlagsChaptersWithoutCanon()
    {
        var result = await Run(_hero.Id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().HaveCount(2); // chương của series khác bị loại
        result.Value![0].ChapterNumber.Should().Be(1);
        result.Value[0].LineCount.Should().Be(2);
        result.Value[0].IsCanonSatisfied.Should().BeTrue();
        result.Value[1].ChapterNumber.Should().Be(3);
        result.Value[1].LineCount.Should().Be(1);
        result.Value[1].IsCanonSatisfied.Should().BeFalse();
    }

    [Fact]
    public async Task EntryWithoutUsages_ReturnsEmpty()
    {
        var result = await Run(_term.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task EntryOfAnotherSeries_IsNotFound()
    {
        var result = await Run(_hero.Id, seriesId: Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
    }
}
