using FluentAssertions;
using PanelForge.Domain.Entities.Content;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class ChapterAndSceneTests
{
    [Fact]
    public void Chapter_Create_ValidParameters_ShouldInstantiateProperly()
    {
        // Arrange & Act
        var seriesId = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 12, 31);
        var chapter = Chapter.Create(seriesId, 1.5m, "The Beginning", targetDate);

        // Assert
        chapter.Should().NotBeNull();
        chapter.SeriesId.Should().Be(seriesId);
        chapter.ChapterNumber.Should().Be(1.5m);
        chapter.Title.Should().Be("The Beginning");
        chapter.TargetReleaseDate.Should().Be(targetDate);
        chapter.VersionVector.Should().Be(0);

        // Act increment version
        chapter.IncrementVersion();
        chapter.VersionVector.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Chapter_Create_InvalidChapterNumber_ShouldThrowArgumentOutOfRangeException(decimal invalidNumber)
    {
        // Act
        Action act = () => Chapter.Create(Guid.NewGuid(), invalidNumber);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Scene_Create_ValidParameters_ShouldInstantiateProperly()
    {
        // Arrange & Act
        var chapterId = Guid.NewGuid();
        var scene = Scene.Create(chapterId, 1, "Intro Scene", "Summary of scene 1");

        // Assert
        scene.Should().NotBeNull();
        scene.ChapterId.Should().Be(chapterId);
        scene.SceneNumber.Should().Be(1);
        scene.Heading.Should().Be("Intro Scene");
        scene.Summary.Should().Be("Summary of scene 1");

        // Update content
        scene.UpdateContent("Revised Heading", "Updated Summary");
        scene.Heading.Should().Be("Revised Heading");
        scene.Summary.Should().Be("Updated Summary");

        // Reorder
        scene.Reorder(2);
        scene.SceneNumber.Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Scene_Create_InvalidSceneNumber_ShouldThrowArgumentOutOfRangeException(int invalidNumber)
    {
        // Act
        Action act = () => Scene.Create(Guid.NewGuid(), invalidNumber);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
