using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Pages.GetPagesByChapter;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Pages;

public class GetPagesByChapterQueryHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;
    private readonly GetPagesByChapterQueryHandler _handler;

    public GetPagesByChapterQueryHandlerTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
        _handler = new GetPagesByChapterQueryHandler(_dbContextMock.Object);
    }

    [Fact]
    public async Task Handle_WhenChapterExists_ShouldReturnPagesOrderedByPageNumber()
    {
        // Arrange
        var chapterId = Guid.NewGuid();
        var chapter = Chapter.Create(Guid.NewGuid(), 1, "Chapter 1");
        typeof(Chapter).GetProperty("Id")!.SetValue(chapter, chapterId);

        var page2 = Page.CreateNew(chapterId, 2, LayoutFormat.StandardPage, 1200, 1800, 300);
        var page1 = Page.CreateNew(chapterId, 1, LayoutFormat.StandardPage, 1200, 1800, 300);

        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(new List<Chapter> { chapter });
        var pagesDbSet = DbSetMockHelper.CreateDbSetMock(new List<Page> { page2, page1 }); // Unordered input

        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);
        _dbContextMock.Setup(db => db.Pages).Returns(pagesDbSet);

        var query = new GetPagesByChapterQuery(chapterId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value![0].PageNumber.Should().Be(1);
        result.Value[1].PageNumber.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WhenChapterDoesNotExist_ShouldReturnFailure()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(new List<Chapter>());
        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);

        var query = new GetPagesByChapterQuery(nonExistentId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("không tồn tại");
    }
}
