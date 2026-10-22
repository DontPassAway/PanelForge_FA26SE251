using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Panels.Commands.CreatePanel;
using PanelForge.Application.Features.Panels.Commands.UpdatePanelCoordinates;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Panels;

public class PanelAndElementCommandTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;

    public PanelAndElementCommandTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
        _dbContextMock.Setup(db => db.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter>()));
    }

    [Fact]
    public async Task CreatePanel_WhenPageExists_ShouldCreateWithPolygonCoordinates()
    {
        // Arrange
        var pageId = Guid.NewGuid();
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        typeof(Page).GetProperty("Id")!.SetValue(page, pageId);

        var pagesDbSet = DbSetMockHelper.CreateDbSetMock(new List<Page> { page });
        var panelsDbSet = DbSetMockHelper.CreateDbSetMock(new List<Panel>());

        _dbContextMock.Setup(db => db.Pages).Returns(pagesDbSet);
        _dbContextMock.Setup(db => db.Panels).Returns(panelsDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreatePanelCommandHandler(_dbContextMock.Object);
        var command = new CreatePanelCommand(
            PageId: pageId,
            UserId: Guid.NewGuid(),
            PanelNumber: 1,
            ReadingOrder: 1,
            BoundingBox: "{\"points\":[{\"x\":0,\"y\":0},{\"x\":100,\"y\":0},{\"x\":100,\"y\":100},{\"x\":0,\"y\":100}]}"
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.PanelNumber.Should().Be(1);
        result.Value.BoundingBox.Should().Contain("points");
    }

    [Fact]
    public async Task UpdatePanelCoordinates_ShouldUpdatePolygonAndReadingOrder()
    {
        // Arrange
        var panelId = Guid.NewGuid();
        var panel = Panel.Create(Guid.NewGuid(), 1, 1, "{}");
        typeof(Panel).GetProperty("Id")!.SetValue(panel, panelId);

        var panelsDbSet = DbSetMockHelper.CreateDbSetMock(new List<Panel> { panel });
        _dbContextMock.Setup(db => db.Panels).Returns(panelsDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        // Khung đổi → handler tìm việc đã duyệt của Panel để trả về làm lại (CF4); ở đây chưa có việc nào
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        typeof(Page).GetProperty("Id")!.SetValue(page, panel.PageId);
        _dbContextMock.Setup(db => db.Pages).Returns(DbSetMockHelper.CreateDbSetMock(new List<Page> { page }));
        _dbContextMock.Setup(db => db.Assignments).Returns(
            DbSetMockHelper.CreateDbSetMock(new List<PanelForge.Domain.Entities.Workflow.Assignment>()));

        var handler = new UpdatePanelCoordinatesCommandHandler(_dbContextMock.Object);
        var command = new UpdatePanelCoordinatesCommand(
            PanelId: panelId,
            UserId: Guid.NewGuid(),
            BoundingBox: "{\"x\":10,\"y\":20,\"w\":300,\"h\":400}",
            ReadingOrder: 2
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.BoundingBox.Should().Be("{\"x\":10,\"y\":20,\"w\":300,\"h\":400}");
        result.Value.ReadingOrder.Should().Be(2);
    }
}
