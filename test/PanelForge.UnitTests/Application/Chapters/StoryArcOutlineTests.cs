using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Chapters.Commands.SetChapterOutline;
using PanelForge.Application.Features.Series.Commands.SetSeriesStoryArc;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Chapters;

/// <summary>V1.2: Producer/Writer ghi story arc (Series) và outline (Chapter) qua endpoint hẹp.</summary>
public class StoryArcOutlineTests
{
    [Fact]
    public async Task SetStoryArc_SavesTrimmed_AndEmptyClears()
    {
        var db = InMemoryDb.Create();
        var series = Series.Create(Guid.NewGuid(), "Series", ReadingDirection.RightToLeft);
        db.Series.Add(series);
        await db.SaveChangesAsync();
        var handler = new SetSeriesStoryArcCommandHandler(db);

        (await handler.Handle(new(series.Id, Guid.NewGuid(), "  Arc 1  "), default)).Value.Should().Be("Arc 1");
        series.StoryArc.Should().Be("Arc 1");
        (await handler.Handle(new(series.Id, Guid.NewGuid(), " "), default)).Value.Should().BeNull();
        series.StoryArc.Should().BeNull();
    }

    [Fact]
    public async Task SetOutline_OnLockedChapter_IsConflict()
    {
        var db = InMemoryDb.Create();
        var chapter = Chapter.Create(Guid.NewGuid(), 1).InProduction();
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync();
        var handler = new SetChapterOutlineCommandHandler(db);

        (await handler.Handle(new(chapter.Id, Guid.NewGuid(), "Mở đầu"), default)).Value.Should().Be("Mở đầu");

        chapter.Approve();
        var locked = await handler.Handle(new(chapter.Id, Guid.NewGuid(), "Sửa"), default);
        locked.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        chapter.Outline.Should().Be("Mở đầu");
    }
}
