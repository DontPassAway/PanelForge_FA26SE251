using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Series.Commands.CreateSeries;
using PanelForge.Application.Features.Series.Commands.DeleteSeries;
using PanelForge.Application.Features.Series.Commands.UpdateSeries;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.SeriesTests;

public class SeriesCommandTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;

    public SeriesCommandTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
    }

    [Fact]
    public async Task CreateSeries_WhenWorkspaceExists_ShouldCreateSeriesAndBible()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid());
        typeof(StudioWorkspace).GetProperty("Id")!.SetValue(workspace, workspaceId);

        var workspacesDbSet = DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace });
        var seriesList = new List<Series>();
        var seriesDbSet = DbSetMockHelper.CreateDbSetMock(seriesList);
        var biblesDbSet = DbSetMockHelper.CreateDbSetMock(new List<SeriesBible>());

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(workspacesDbSet);
        _dbContextMock.Setup(db => db.Series).Returns(seriesDbSet);
        _dbContextMock.Setup(db => db.SeriesBibles).Returns(biblesDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateSeriesCommandHandler(_dbContextMock.Object);
        var command = new CreateSeriesCommand(
            WorkspaceId: workspaceId,
            UserId: Guid.NewGuid(),
            Title: "Naruto",
            Synopsis: "Ninja story",
            ReadingDirection: ReadingDirection.RightToLeft,
            PipelineDefinitionId: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Title.Should().Be("Naruto");
        result.Value.WorkspaceId.Should().Be(workspaceId);
    }

    [Fact]
    public async Task UpdateSeries_WhenSeriesExists_ShouldUpdateDetails()
    {
        // Arrange
        var seriesId = Guid.NewGuid();
        var series = Series.Create(Guid.NewGuid(), "Old Title", ReadingDirection.LeftToRight);
        typeof(Series).GetProperty("Id")!.SetValue(series, seriesId);

        var seriesDbSet = DbSetMockHelper.CreateDbSetMock(new List<Series> { series });
        _dbContextMock.Setup(db => db.Series).Returns(seriesDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UpdateSeriesCommandHandler(_dbContextMock.Object);
        var command = new UpdateSeriesCommand(
            SeriesId: seriesId,
            UserId: Guid.NewGuid(),
            Title: "New Title",
            Synopsis: "Updated Synopsis",
            ReadingDirection: ReadingDirection.Vertical,
            PipelineDefinitionId: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("New Title");
        result.Value.ReadingDirection.Should().Be(ReadingDirection.Vertical);
    }

    [Fact]
    public async Task DeleteSeries_WhenSeriesExists_ShouldCallRemoveOnDbContext()
    {
        // Arrange
        var seriesId = Guid.NewGuid();
        var series = Series.Create(Guid.NewGuid(), "To Delete", ReadingDirection.RightToLeft);
        typeof(Series).GetProperty("Id")!.SetValue(series, seriesId);

        var seriesList = new List<Series> { series };
        var seriesDbSet = DbSetMockHelper.CreateDbSetMock(seriesList);
        _dbContextMock.Setup(db => db.Series).Returns(seriesDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeleteSeriesCommandHandler(_dbContextMock.Object);
        var command = new DeleteSeriesCommand(seriesId, Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        seriesList.Should().BeEmpty();
        _dbContextMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
