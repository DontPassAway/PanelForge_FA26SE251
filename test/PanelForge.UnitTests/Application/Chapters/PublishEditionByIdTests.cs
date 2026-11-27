using FluentAssertions;
using MediatR;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Commands.Editions;
using PanelForge.Application.Features.Chapters.Commands.PublishChapter;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;

namespace PanelForge.UnitTests.Application.Chapters;

/// <summary>POST api/editions/{editionId}/publish|schedule: trước đây truyền editionId làm chapterId nên luôn 404 (D1, BR-34).</summary>
public class PublishEditionByIdTests
{
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Chapter _chapter;
    private readonly ChapterEdition _vi;
    private readonly ChapterEdition _en;
    private readonly Guid _user = Guid.NewGuid();

    public PublishEditionByIdTests()
    {
        var series = Series.Create(Guid.NewGuid(), "S", ReadingDirection.RightToLeft);
        _chapter = Chapter.Create(series.Id, 1, "C1");
        _vi = ChapterEdition.Create(_chapter.Id, series.OriginalLanguage, EditionStatus.Approved);
        _en = ChapterEdition.Create(_chapter.Id, "en", EditionStatus.Approved);
        _db.Setup(d => d.ChapterEditions).Returns(DbSetMockHelper.CreateDbSetMock(new List<ChapterEdition> { _vi, _en }));
        _db.Setup(d => d.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter> { _chapter }));
        _db.Setup(d => d.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { series }));
        _sender.Setup(s => s.Send(It.IsAny<PublishChapterCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChapterReviewDto>.Success(null!));
        _sender.Setup(s => s.Send(It.IsAny<PublishEditionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChapterEditionDto>.Success(null!));
    }

    private Task<Result<object>> Run(Guid editionId, DateTime? at = null) =>
        new PublishEditionByIdCommandHandler(_db.Object, _sender.Object).Handle(new PublishEditionByIdCommand(editionId, _user, at), default);

    [Fact]
    public async Task OriginalEdition_PublishesTheChapter_WithSchedule()
    {
        var at = DateTime.UtcNow.AddDays(1);
        (await Run(_vi.Id, at)).IsSuccess.Should().BeTrue();
        _sender.Verify(s => s.Send(It.Is<PublishChapterCommand>(c => c.ChapterId == _chapter.Id && c.PublishAt == at), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task TranslatedEdition_UsesEditionPublish_AndRejectsSchedule()
    {
        (await Run(_en.Id)).IsSuccess.Should().BeTrue();
        _sender.Verify(s => s.Send(It.Is<PublishEditionCommand>(c => c.ChapterId == _chapter.Id && c.LanguageCode == "en"), It.IsAny<CancellationToken>()));

        (await Run(_en.Id, DateTime.UtcNow)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task UnknownEdition_IsNotFound()
    {
        (await Run(Guid.NewGuid())).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        _sender.VerifyNoOtherCalls();
    }
}
