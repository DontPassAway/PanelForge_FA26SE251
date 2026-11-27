using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Commands.MoveElement;
using PanelForge.Application.Features.CanvasElements.Commands.UpdateElementText;
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

    private (Page page, Guid elementId, Guid lettererId, ChapterEdition edition) SetupApprovedChapterWithGridAndEdition(
        EditionStatus editionStatus)
    {
        var lettererId = Guid.NewGuid();
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(_studio.Id, lettererId, WorkspaceRole.Member));
        _db.SeriesMembers.Add(SeriesMember.Create(_series.Id, lettererId, WorkspaceRole.Letterer)); // Spec D10

        var (page, elementId) = SetupPageWithBalloon();
        _db.Pages.Add(page);

        var stage = PipelineStage.Create(Guid.NewGuid(), "Lettering", "lettering", 4, allowedRole: WorkspaceRole.Letterer, isApprovalGate: true);
        _db.PipelineStages.Add(stage);

        var lot = StageTaskLot.Create(_series.Id, _chapter.Id, stage.Id, lettererId, WorkspaceRole.Letterer, _user, "Chèn chữ", null);
        _db.StageTaskLots.Add(lot);

        var task = StageTask.Create(_series.Id, _chapter.Id, stage.Id, page.Id);
        task.RestoreFromLegacy(lot.Id, StageTaskStatus.Approved, DateTime.UtcNow);
        _db.StageTasks.Add(task);

        _chapter.InProduction().Approve();

        var edition = ChapterEdition.Create(_chapter.Id, "en", editionStatus);
        _db.ChapterEditions.Add(edition);
        _db.SaveChanges();

        return (page, elementId, lettererId, edition);
    }

    [Fact]
    public async Task Handler_AllowsLettererWhenEditionDraftAndChapterApprovedWithAllV3StageTasksApproved()
    {
        var (page, elementId, lettererId, edition) = SetupApprovedChapterWithGridAndEdition(EditionStatus.Draft);
        var handler = new UpdateElementTranslationCommandHandler(_repo.Object, _db);

        var result = await handler.Handle(
            new UpdateElementTranslationCommand(page.Id, elementId, "en", "Hello world", lettererId), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        page.Elements[elementId].Texts["en"].Should().Be("Hello world");
        edition.TranslatorUserId.Should().Be(lettererId, "ghi nhận người sửa bản dịch làm TranslatorUserId để giữ BR-06");
    }

    [Fact]
    public async Task Handler_DeniesWhenEditionInReviewEvenWhenChapterApproved()
    {
        var (page, elementId, lettererId, _) = SetupApprovedChapterWithGridAndEdition(EditionStatus.InReview);
        var handler = new UpdateElementTranslationCommandHandler(_repo.Object, _db);

        var result = await handler.Handle(
            new UpdateElementTranslationCommand(page.Id, elementId, "en", "Updated while in review", lettererId), default);

        result.IsSuccess.Should().BeFalse();
        page.Elements[elementId].Texts.Should().NotContainKey("en");
    }

    [Fact]
    public async Task Handler_DeniesForOriginalLanguageWhenChapterApproved()
    {
        var (page, elementId, lettererId, _) = SetupApprovedChapterWithGridAndEdition(EditionStatus.Draft);
        var handler = new UpdateElementTranslationCommandHandler(_repo.Object, _db);

        var result = await handler.Handle(
            new UpdateElementTranslationCommand(page.Id, elementId, "vi", "Sửa chữ gốc qua endpoint dịch", lettererId), default);

        result.IsSuccess.Should().BeFalse();
        page.Elements[elementId].Content.Should().Be("Xin chào thế giới");
    }

    [Fact]
    public async Task OriginalContentAndGeometryCommands_RemainBlockedWhenChapterApprovedAndEditionDraft()
    {
        var (page, elementId, lettererId, _) = SetupApprovedChapterWithGridAndEdition(EditionStatus.Draft);

        var textHandler = new UpdateElementTextCommandHandler(_repo.Object, _db);
        var textResult = await textHandler.Handle(
            new UpdateElementTextCommand(page.Id, elementId, "Sửa chữ gốc", null, lettererId), default);
        textResult.IsSuccess.Should().BeFalse();
        page.Elements[elementId].Content.Should().Be("Xin chào thế giới");

        var moveHandler = new MoveElementCommandHandler(_repo.Object, _db);
        var moveResult = await moveHandler.Handle(
            new MoveElementCommand(page.Id, elementId, 100, 100, 220, 120, null, lettererId), default);
        moveResult.IsSuccess.Should().BeFalse();
        page.Elements[elementId].X.Should().Be(50);
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
