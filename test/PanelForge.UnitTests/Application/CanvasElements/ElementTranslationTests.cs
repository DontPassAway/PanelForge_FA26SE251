using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Commands.UpdateElementTranslation;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.CanvasElements;

public class ElementTranslationTests
{
    private readonly Mock<IPageRepository> _repo = new();
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Guid _user = Guid.NewGuid();

    public ElementTranslationTests()
    {
        _studio = StudioWorkspace.Create("Test Studio", _user);
        _db.StudioWorkspaces.Add(_studio);
        _series = DomainSeries.Create(_studio.Id, "Test Series", ReadingDirection.LeftToRight);
        _db.Series.Add(_series);
        _chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(_chapter);
        _db.SaveChanges();
    }

    private (Page page, Guid elementId) SetupPageWithBalloon()
    {
        var page = Page.CreateNew(_chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        page.AddElement(Page.TextElementTypes.First(), 50, 50, 200, 100, 1, _user, content: "Xin chào thế giới");
        var elementId = page.Elements.Keys.Single();
        _repo.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>())).ReturnsAsync(page);
        return (page, elementId);
    }

    [Fact]
    public void Page_UpdateElementTranslation_SetsTextsAndIncrementsVersion()
    {
        var (page, elementId) = SetupPageWithBalloon();
        page.Elements[elementId].Version.Should().Be(1);
        page.Elements[elementId].Texts.Should().BeEmpty();

        page.UpdateElementTranslation(elementId, "en", "Hello world", _user);

        page.Elements[elementId].Version.Should().Be(2);
        page.Elements[elementId].Texts.Should().ContainKey("en");
        page.Elements[elementId].Texts["en"].Should().Be("Hello world");
        page.Elements[elementId].Content.Should().Be("Xin chào thế giới", "chữ gốc không bị ghi đè");
    }

    [Fact]
    public void Page_UpdateElementTranslation_WithEmptyText_RemovesKey()
    {
        var (page, elementId) = SetupPageWithBalloon();
        page.UpdateElementTranslation(elementId, "en", "Hello", _user);
        page.Elements[elementId].Texts.Should().ContainKey("en");

        page.UpdateElementTranslation(elementId, "en", "", _user);
        page.Elements[elementId].Texts.Should().NotContainKey("en");
        page.Elements[elementId].Version.Should().Be(3);
    }

    [Fact]
    public async Task Handler_RejectsWhenLanguageNotInReleaseLanguages()
    {
        var (page, elementId) = SetupPageWithBalloon();
        var handler = new UpdateElementTranslationCommandHandler(_repo.Object, _db);

        var result = await handler.Handle(
            new UpdateElementTranslationCommand(page.Id, elementId, "fr", "Bonjour", _user), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Handler_RejectsWhenLanguageIsOriginalLanguage()
    {
        var (page, elementId) = SetupPageWithBalloon();
        var handler = new UpdateElementTranslationCommandHandler(_repo.Object, _db);

        var result = await handler.Handle(
            new UpdateElementTranslationCommand(page.Id, elementId, "vi", "Chào", _user), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Handler_RejectsWhenExpectedVersionMismatch()
    {
        var (page, elementId) = SetupPageWithBalloon();
        var handler = new UpdateElementTranslationCommandHandler(_repo.Object, _db);

        var result = await handler.Handle(
            new UpdateElementTranslationCommand(page.Id, elementId, "en", "Hello", _user, ExpectedElementVersion: 99), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task Handler_RejectsWhenPencilPending_BR19()
    {
        var (page, elementId) = SetupPageWithBalloon();
        var stage = PipelineStage.Create(Guid.NewGuid(), "Pencil", "pencil", 1);
        _db.PipelineStages.Add(stage);
        var assignment = Assignment.Create(
            _series.Id, _chapter.Id, WorkflowEntityType.Page, page.Id, stage.Id, _user, WorkspaceRole.Artist,
            "Draw pencil artwork");
        assignment.StartWork();
        _db.Assignments.Add(assignment);
        await _db.SaveChangesAsync();

        var handler = new UpdateElementTranslationCommandHandler(_repo.Object, _db);

        var result = await handler.Handle(
            new UpdateElementTranslationCommand(page.Id, elementId, "en", "Hello", _user), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Pencil");
    }

    [Fact]
    public async Task Handler_AllowsTranslationAfterChapterApproved_BR34()
    {
        // Nộp bản dịch đòi chương ChapterApproved → phải nhập/sửa được bản dịch khi chương đã khóa tranh.
        _chapter.InProduction().Approve();
        _db.SaveChanges();
        var (page, elementId) = SetupPageWithBalloon();
        var handler = new UpdateElementTranslationCommandHandler(_repo.Object, _db);

        var result = await handler.Handle(
            new UpdateElementTranslationCommand(page.Id, elementId, "en", "Hello", _user), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        page.Elements[elementId].Texts["en"].Should().Be("Hello");
    }

    [Fact]
    public void LetteringWarningRules_EvaluatesTextOverflowForGivenLanguage()
    {
        var (page, elementId) = SetupPageWithBalloon();
        // small box: width 50, height 50
        page.Elements[elementId].Width = 50;
        page.Elements[elementId].Height = 50;
        page.Elements[elementId].Texts["en"] = new string('A', 100);

        var warnings = LetteringWarningRules.Evaluate(
            page.Elements.Values.ToList(), ReadingDirection.LeftToRight, new Dictionary<Guid, (int, int)>(), lang: "en");

        warnings.Should().Contain(w => w.Code == LetteringWarningRules.TextOverflow && w.Message.Contains("[en]"));
    }
}
