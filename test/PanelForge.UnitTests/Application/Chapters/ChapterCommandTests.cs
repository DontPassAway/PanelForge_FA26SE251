using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Chapters.Commands.CreateChapter;
using PanelForge.Application.Features.Chapters.Commands.DeleteChapter;
using PanelForge.Application.Features.Chapters.Commands.UpdateChapter;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.ChapterTests;

public class ChapterCommandTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;

    public ChapterCommandTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
    }

    [Fact]
    public async Task CreateChapter_WhenSeriesExistsAndNumberNotDuplicate_ShouldCreateSuccessfully()
    {
        // Arrange
        var seriesId = Guid.NewGuid();
        var series = Series.Create(Guid.NewGuid(), "Manga", ReadingDirection.RightToLeft);
        typeof(Series).GetProperty("Id")!.SetValue(series, seriesId);

        var seriesDbSet = DbSetMockHelper.CreateDbSetMock(new List<Series> { series });
        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(new List<Chapter>());

        _dbContextMock.Setup(db => db.Series).Returns(seriesDbSet);
        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateChapterCommandHandler(_dbContextMock.Object);
        var command = new CreateChapterCommand(
            SeriesId: seriesId,
            UserId: Guid.NewGuid(),
            ChapterNumber: 1,
            Title: "Chapter 1: The Beginning",
            TargetReleaseDate: new DateOnly(2026, 12, 1)
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ChapterNumber.Should().Be(1);
        result.Value.Title.Should().Be("Chapter 1: The Beginning");
    }

    [Fact]
    public async Task CreateChapter_WhenNumberDuplicate_ShouldReturnFailure()
    {
        // Arrange
        var seriesId = Guid.NewGuid();
        var series = Series.Create(Guid.NewGuid(), "Manga", ReadingDirection.RightToLeft);
        typeof(Series).GetProperty("Id")!.SetValue(series, seriesId);

        var existingChapter = Chapter.Create(seriesId, 1, "Existing Chapter");
        var seriesDbSet = DbSetMockHelper.CreateDbSetMock(new List<Series> { series });
        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(new List<Chapter> { existingChapter });

        _dbContextMock.Setup(db => db.Series).Returns(seriesDbSet);
        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);

        var handler = new CreateChapterCommandHandler(_dbContextMock.Object);
        var command = new CreateChapterCommand(
            SeriesId: seriesId,
            UserId: Guid.NewGuid(),
            ChapterNumber: 1,
            Title: "Duplicate Chapter"
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("đã tồn tại");
    }

    [Fact]
    public async Task UpdateChapter_WhenIncrementVersionIsTrue_ShouldIncrementVersionVector()
    {
        // Arrange
        var chapterId = Guid.NewGuid();
        var chapter = Chapter.Create(Guid.NewGuid(), 1, "Initial Chapter");
        typeof(Chapter).GetProperty("Id")!.SetValue(chapter, chapterId);

        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(new List<Chapter> { chapter });
        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UpdateChapterCommandHandler(_dbContextMock.Object);
        var command = new UpdateChapterCommand(
            ChapterId: chapterId,
            UserId: Guid.NewGuid(),
            Title: "Updated Title",
            TargetReleaseDate: null,
            IncrementVersion: true
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.VersionVector.Should().Be(1);
        result.Value.Title.Should().Be("Updated Title");
    }

    [Fact]
    public async Task DeleteChapter_WhenChapterExists_ShouldCallRemove()
    {
        // Arrange
        var chapterId = Guid.NewGuid();
        var chapter = Chapter.Create(Guid.NewGuid(), 1, "To Delete");
        typeof(Chapter).GetProperty("Id")!.SetValue(chapter, chapterId);

        var chapterList = new List<Chapter> { chapter };
        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(chapterList);
        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeleteChapterCommandHandler(_dbContextMock.Object);
        var command = new DeleteChapterCommand(chapterId, Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        chapterList.Should().BeEmpty();
        _dbContextMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
