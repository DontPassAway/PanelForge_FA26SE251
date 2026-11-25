using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Pages.Queries.SplitArtwork;
using PanelForge.Domain.Entities.Content;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Pages;

public class SplitArtworkQueryTests
{
    private readonly Page _page = Page.CreateNew(Guid.NewGuid(), 1, default, 1200, 1800, 300);
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly Mock<IPanelSplitter> _splitter = new();

    public SplitArtworkQueryTests() =>
        _db.Setup(d => d.Pages).Returns(DbSetMockHelper.CreateDbSetMock(new List<Page> { _page }));

    private Task<Result<PanelSplitResult>> Handle(Guid pageId) =>
        new SplitArtworkQueryHandler(_db.Object, _splitter.Object)
            .Handle(new SplitArtworkQuery(pageId, new MemoryStream([1]), "p.png", true), CancellationToken.None);

    [Fact]
    public async Task UnknownPage_ReturnsNotFound_WithoutCallingSplitter()
    {
        var result = await Handle(Guid.NewGuid());

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        _splitter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidImage_ReturnsValidation()
    {
        _splitter.Setup(s => s.SplitAsync(It.IsAny<Stream>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidImageException("File không phải ảnh hợp lệ."));

        var result = await Handle(_page.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task ValidImage_ReturnsSplitterResult()
    {
        var expected = new PanelSplitResult(10, 20, [new SplitPanelDto(1, [0, 0, 10, 20])], []);
        _splitter.Setup(s => s.SplitAsync(It.IsAny<Stream>(), "p.png", true, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await Handle(_page.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task UnexpectedSplitterFailure_IsNotSwallowed()
    {
        _splitter.Setup(s => s.SplitAsync(It.IsAny<Stream>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("native crash"));

        await FluentActions.Awaiting(() => Handle(_page.Id)).Should().ThrowAsync<InvalidOperationException>();
    }
}
