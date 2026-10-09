using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Pages.Commands.DeletePage;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Pages;

public class DeletePageCommandHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock = new();
    private readonly Mock<IPageRepository> _pageRepositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly PassThroughUnitOfWork _unitOfWork = new();
    private readonly DeletePageCommandHandler _handler;

    public DeletePageCommandHandlerTests()
    {
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _handler = new DeletePageCommandHandler(
            _dbContextMock.Object, _pageRepositoryMock.Object, _unitOfWork, _currentUserMock.Object);
    }

    [Fact]
    public async Task Handle_WhenPageExists_ShouldDeleteInMartenAndEfInOneTransaction()
    {
        var userId = Guid.NewGuid();
        _currentUserMock.Setup(u => u.UserId).Returns(userId);

        var streamPage = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        streamPage.ClearUncommittedEvents();
        var efPages = new List<Page> { streamPage };
        _dbContextMock.Setup(db => db.Pages).Returns(DbSetMockHelper.CreateDbSetMock(efPages));
        _pageRepositoryMock.Setup(r => r.GetAsync(streamPage.Id, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(streamPage);

        var result = await _handler.Handle(new DeletePageCommand(streamPage.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        streamPage.IsDeleted.Should().BeTrue();
        streamPage.DeletedBy.Should().Be(userId.ToString());
        efPages.Should().BeEmpty();
        _unitOfWork.ExecutionCount.Should().Be(1);
        _pageRepositoryMock.Verify(r => r.SaveAsync(streamPage, It.IsAny<CancellationToken>()), Times.Once);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPageMissingEverywhere_ShouldReturnFailure()
    {
        var pageId = Guid.NewGuid();
        _dbContextMock.Setup(db => db.Pages).Returns(DbSetMockHelper.CreateDbSetMock(new List<Page>()));
        _pageRepositoryMock.Setup(r => r.GetAsync(pageId, It.IsAny<CancellationToken>()))
                           .ReturnsAsync((Page?)null);

        var result = await _handler.Handle(new DeletePageCommand(pageId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _pageRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
