using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Pages.CreatePage;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Pages;

public class CreatePageCommandHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;
    private readonly Mock<IPageRepository> _pageRepositoryMock;
    private readonly CreatePageCommandHandler _handler;

    public CreatePageCommandHandlerTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
        _pageRepositoryMock = new Mock<IPageRepository>();
        _handler = new CreatePageCommandHandler(_dbContextMock.Object, _pageRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WhenChapterExists_ShouldCreatePage_SaveToMartenAndEf_AndReturnSuccess()
    {
        // Arrange
        var chapterId = Guid.NewGuid();
        var chapter = Chapter.Create(Guid.NewGuid(), 1, "Chapter 1");
        // Gán Id cho Chapter thông qua reflection hoặc helper
        typeof(Chapter).GetProperty("Id")!.SetValue(chapter, chapterId);

        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(new List<Chapter> { chapter });
        var pagesList = new List<Page>();
        var pagesDbSet = DbSetMockHelper.CreateDbSetMock(pagesList);

        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);
        _dbContextMock.Setup(db => db.Pages).Returns(pagesDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(1);

        var command = new CreatePageCommand(
            ChapterId:    chapterId,
            PageNumber:   1,
            LayoutFormat: "StandardPage",
            WidthPx:      1200,
            HeightPx:     1800,
            Dpi:          300);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.ChapterId.Should().Be(chapterId);
        result.Value.PageNumber.Should().Be(1);
        result.Value.LayoutFormat.Should().Be("StandardPage");

        // Verify Marten Write Model
        _pageRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify EF Core Read Model
        pagesList.Should().ContainSingle(p => p.ChapterId == chapterId && p.PageNumber == 1);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenChapterDoesNotExist_ShouldReturnFailure()
    {
        // Arrange
        var nonExistentChapterId = Guid.NewGuid();
        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(new List<Chapter>());

        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);

        var command = new CreatePageCommand(ChapterId: nonExistentChapterId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không tồn tại");
        _pageRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPageNumberDuplicate_ShouldReturnFailure()
    {
        // Arrange
        var chapterId = Guid.NewGuid();
        var chapter = Chapter.Create(Guid.NewGuid(), 1, "Chapter 1");
        typeof(Chapter).GetProperty("Id")!.SetValue(chapter, chapterId);

        var existingPage = Page.Create(chapterId, 1, PanelForge.Domain.Enums.LayoutFormat.StandardPage, 1200, 1800, 300);

        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(new List<Chapter> { chapter });
        var pagesDbSet = DbSetMockHelper.CreateDbSetMock(new List<Page> { existingPage });

        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);
        _dbContextMock.Setup(db => db.Pages).Returns(pagesDbSet);

        var command = new CreatePageCommand(
            ChapterId:  chapterId,
            PageNumber: 1 // Trùng số trang 1
        );

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("đã tồn tại");
        _pageRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
