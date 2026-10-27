using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Series.Queries.GetSeriesById;
using PanelForge.Application.Features.Series.Queries.GetSeriesList;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.SeriesTests;

public class SeriesQueryAuthorizationTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;
    private readonly Mock<IWorkspaceAuthorizationService> _authServiceMock;

    public SeriesQueryAuthorizationTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
        _authServiceMock = new Mock<IWorkspaceAuthorizationService>();
    }

    [Fact]
    public async Task GetSeriesList_WhenUserIsWorkspaceOwnerOrProducer_ReturnsAllSeriesInWorkspace()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var producerId = Guid.NewGuid();

        var s1 = Series.Create(workspaceId, "Series 1", ReadingDirection.RightToLeft);
        var s2 = Series.Create(workspaceId, "Series 2", ReadingDirection.RightToLeft);

        var seriesList = new List<Series> { s1, s2 };
        _dbContextMock.Setup(db => db.Series).Returns(DbSetMockHelper.CreateDbSetMock(seriesList));

        _authServiceMock
            .Setup(a => a.IsOwnerOrProducerAsync(producerId, workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new GetSeriesListQueryHandler(_dbContextMock.Object, _authServiceMock.Object);
        var query = new GetSeriesListQuery(workspaceId, producerId);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSeriesList_WhenUserIsMember_OnlyReturnsSeriesWhereUserIsAssigned()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var writerId = Guid.NewGuid();

        var s1 = Series.Create(workspaceId, "Assigned Series", ReadingDirection.RightToLeft);
        var member = SeriesMember.Create(s1.Id, writerId, WorkspaceRole.Writer);
        s1.Members.Add(member);

        var s2 = Series.Create(workspaceId, "Unassigned Series", ReadingDirection.RightToLeft);

        var seriesList = new List<Series> { s1, s2 };
        _dbContextMock.Setup(db => db.Series).Returns(DbSetMockHelper.CreateDbSetMock(seriesList));

        _authServiceMock
            .Setup(a => a.IsOwnerOrProducerAsync(writerId, workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new GetSeriesListQueryHandler(_dbContextMock.Object, _authServiceMock.Object);
        var query = new GetSeriesListQuery(workspaceId, writerId);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].Title.Should().Be("Assigned Series");
    }

    [Fact]
    public async Task GetSeriesById_WhenUserIsSeriesMember_ReturnsSuccess()
    {
        // Arrange
        var seriesId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var series = Series.Create(Guid.NewGuid(), "Series Title", ReadingDirection.RightToLeft);
        typeof(Series).GetProperty("Id")!.SetValue(series, seriesId);

        _dbContextMock.Setup(db => db.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { series }));

        _authServiceMock
            .Setup(a => a.IsSeriesMemberAsync(userId, seriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new GetSeriesByIdQueryHandler(_dbContextMock.Object, _authServiceMock.Object);
        var query = new GetSeriesByIdQuery(seriesId, userId);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("Series Title");
    }

    [Fact]
    public async Task GetSeriesById_WhenUserIsNotSeriesMember_ReturnsForbidden()
    {
        // Arrange
        var seriesId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _authServiceMock
            .Setup(a => a.IsSeriesMemberAsync(userId, seriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new GetSeriesByIdQueryHandler(_dbContextMock.Object, _authServiceMock.Object);
        var query = new GetSeriesByIdQuery(seriesId, userId);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("Bạn không có quyền truy cập");
    }
}
