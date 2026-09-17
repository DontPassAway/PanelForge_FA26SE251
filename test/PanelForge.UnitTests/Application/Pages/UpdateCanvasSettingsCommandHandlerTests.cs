using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Pages.UpdateCanvasSettings;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Pages;

public class UpdateCanvasSettingsCommandHandlerTests
{
    private readonly Mock<IPageRepository> _pageRepositoryMock;
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;
    private readonly UpdateCanvasSettingsCommandHandler _handler;

    public UpdateCanvasSettingsCommandHandlerTests()
    {
        _pageRepositoryMock = new Mock<IPageRepository>();
        _dbContextMock = new Mock<IPanelForgeDbContext>();
        _handler = new UpdateCanvasSettingsCommandHandler(_pageRepositoryMock.Object, _dbContextMock.Object);
    }

    [Fact]
    public async Task Handle_WhenPageExists_ShouldUpdateSettingsInMartenAndEfCore()
    {
        // Arrange
        var page = Page.Create(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        _pageRepositoryMock.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(page);

        var pagesDbSet = DbSetMockHelper.CreateDbSetMock(new List<Page> { page });
        _dbContextMock.Setup(db => db.Pages).Returns(pagesDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(1);

        var command = new UpdateCanvasSettingsCommand(
            PageId:       page.Id,
            WidthPx:      1600,
            HeightPx:     2400,
            Dpi:          600,
            LayoutFormat: "WebtoonLongstrip"
        );

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        page.WidthPx.Should().Be(1600);
        page.HeightPx.Should().Be(2400);
        page.Dpi.Should().Be(600);
        page.LayoutFormat.Should().Be(LayoutFormat.WebtoonLongstrip);

        _pageRepositoryMock.Verify(r => r.SaveAsync(page, It.IsAny<CancellationToken>()), Times.Once);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPageDoesNotExist_ShouldReturnFailure()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        _pageRepositoryMock.Setup(r => r.GetAsync(nonExistentId, It.IsAny<CancellationToken>()))
                           .ReturnsAsync((Page?)null);

        var command = new UpdateCanvasSettingsCommand(nonExistentId, 1000, 1000, 300, "StandardPage");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không tồn tại");
    }
}
