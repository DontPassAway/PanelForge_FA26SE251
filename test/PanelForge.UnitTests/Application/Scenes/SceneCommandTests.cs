using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Scenes.Commands.CreateScene;
using PanelForge.Application.Features.Scenes.Commands.DeleteScene;
using PanelForge.Application.Features.Scenes.Commands.ReorderScenes;
using PanelForge.Application.Features.Scenes.Commands.UpdateScene;
using PanelForge.Application.Features.Scenes.Models;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.SceneTests;

public class SceneCommandTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;

    public SceneCommandTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
    }

    [Fact]
    public async Task CreateScene_WhenChapterExists_ShouldCreateWithAutoIncrementNumber()
    {
        // Arrange
        var chapterId = Guid.NewGuid();
        var chapter = Chapter.Create(Guid.NewGuid(), 1, "Chapter 1");
        typeof(Chapter).GetProperty("Id")!.SetValue(chapter, chapterId);

        var existingScene = Scene.Create(chapterId, 1, "Scene 1");
        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(new List<Chapter> { chapter });
        var scenesDbSet = DbSetMockHelper.CreateDbSetMock(new List<Scene> { existingScene });

        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);
        _dbContextMock.Setup(db => db.Scenes).Returns(scenesDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateSceneCommandHandler(_dbContextMock.Object);
        var command = new CreateSceneCommand(
            ChapterId: chapterId,
            UserId: Guid.NewGuid(),
            SceneNumber: null, // Tự động tăng lên 2
            Heading: "Scene 2: Market Place",
            Summary: "Luffy visits the local market"
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.SceneNumber.Should().Be(2);
        result.Value.Heading.Should().Be("Scene 2: Market Place");
    }

    [Fact]
    public async Task ReorderScenes_ShouldUpdateSceneNumbers()
    {
        // Arrange
        var chapterId = Guid.NewGuid();
        var scene1 = Scene.Create(chapterId, 1, "Scene 1");
        var scene2 = Scene.Create(chapterId, 2, "Scene 2");
        var scene1Id = Guid.NewGuid();
        var scene2Id = Guid.NewGuid();
        typeof(Scene).GetProperty("Id")!.SetValue(scene1, scene1Id);
        typeof(Scene).GetProperty("Id")!.SetValue(scene2, scene2Id);

        var scenesDbSet = DbSetMockHelper.CreateDbSetMock(new List<Scene> { scene1, scene2 });
        _dbContextMock.Setup(db => db.Scenes).Returns(scenesDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new ReorderScenesCommandHandler(_dbContextMock.Object);
        var command = new ReorderScenesCommand(
            ChapterId: chapterId,
            UserId: Guid.NewGuid(),
            Scenes: new List<ReorderSceneItem>
            {
                new(scene1Id, 2),
                new(scene2Id, 1)
            }
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        scene1.SceneNumber.Should().Be(2);
        scene2.SceneNumber.Should().Be(1);
    }
}
