using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Commands.ApproveChapter;
using PanelForge.Application.Features.Chapters.Commands.Editions;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Chapters;

public class ChapterEditionTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IPageRepository> _pages = new();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly Guid _editorId = Guid.NewGuid();
    private readonly Guid _lettererId = Guid.NewGuid();
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Page _page;

    public ChapterEditionTests()
    {
        var studio = StudioWorkspace.Create("Studio Moon", _producerId);
        _db.StudioWorkspaces.Add(studio);
        _db.WorkspaceMembers.AddRange(
            WorkspaceMember.Create(studio.Id, _producerId, WorkspaceRole.Producer),
            WorkspaceMember.Create(studio.Id, _editorId, WorkspaceRole.Editor),
            WorkspaceMember.Create(studio.Id, _lettererId, WorkspaceRole.Letterer));

        _series = DomainSeries.Create(studio.Id, "Moon Blade", ReadingDirection.RightToLeft, genre: "Fantasy");
        _series.SetLanguages("vi", ["vi", "en"]);
        _db.Series.Add(_series);

        _chapter = Chapter.Create(_series.Id, 1, "Khởi đầu").InProduction();
        _db.Chapters.Add(_chapter);

        _page = Page.CreateNew(_chapter.Id, 1, default, 1200, 1800, 300);
        _db.Pages.Add(_page);

        // Add an edition
        var enEdition = ChapterEdition.Create(_chapter.Id, "en", EditionStatus.Draft);
        _db.ChapterEditions.Add(enEdition);

        _db.SaveChanges();
    }

    [Fact]
    public async Task Submit_WhenChapterNotApproved_ReturnsConflict()
    {
        var handler = new SubmitEditionTranslationCommandHandler(_db, _pages.Object);
        var result = await handler.Handle(new SubmitEditionTranslationCommand(_chapter.Id, "en", _lettererId), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("ChapterApproved");
    }

    [Fact]
    public async Task Submit_WhenUntranslatedElementsExist_ReturnsConflict()
    {
        // Approve chapter first
        await new ApproveChapterCommandHandler(_db, _pages.Object).Handle(new ApproveChapterCommand(_chapter.Id, _producerId), default);

        // Add a text element to page
        var page = Page.CreateNew(_chapter.Id, 1, default, 1200, 1800, 300);
        page.AddElement(Page.TextElementTypes.First(), 50, 50, 200, 100, 1, _producerId, content: "Xin chào");
        _pages.Setup(p => p.GetAsync(_page.Id, It.IsAny<CancellationToken>())).ReturnsAsync(page);

        var handler = new SubmitEditionTranslationCommandHandler(_db, _pages.Object);
        var result = await handler.Handle(new SubmitEditionTranslationCommand(_chapter.Id, "en", _lettererId), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Chưa đủ bản dịch");
    }

    [Fact]
    public async Task Submit_WhenAllElementsTranslated_TransitionsToInReview()
    {
        // Approve chapter
        await new ApproveChapterCommandHandler(_db, _pages.Object).Handle(new ApproveChapterCommand(_chapter.Id, _producerId), default);

        // Add a text element with EN translation
        var page = Page.CreateNew(_chapter.Id, 1, default, 1200, 1800, 300);
        page.AddElement(Page.TextElementTypes.First(), 50, 50, 200, 100, 1, _producerId, content: "Xin chào");
        var elementId = page.Elements.Keys.Single();
        page.UpdateElementTranslation(elementId, "en", "Hello", _lettererId);
        _pages.Setup(p => p.GetAsync(_page.Id, It.IsAny<CancellationToken>())).ReturnsAsync(page);

        var handler = new SubmitEditionTranslationCommandHandler(_db, _pages.Object);
        var result = await handler.Handle(new SubmitEditionTranslationCommand(_chapter.Id, "en", _lettererId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(EditionStatus.InReview);
        result.Value.TranslatorUserId.Should().Be(_lettererId);
    }

    [Fact]
    public async Task Review_WhenEditorIsTranslator_ReturnsForbidden_BR06()
    {
        // Set edition to InReview with Editor as Translator
        var edition = _db.ChapterEditions.First(e => e.ChapterId == _chapter.Id && e.LanguageCode == "en");
        edition.SubmitTranslation(_editorId);
        _db.SaveChanges();

        var handler = new ReviewEditionCommandHandler(_db);
        var result = await handler.Handle(new ReviewEditionCommand(_chapter.Id, "en", _editorId, ReviewEditionAction.Approve), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("BR-06");
    }

    [Fact]
    public async Task Review_WhenEditorApproves_TransitionsToEditorApproved()
    {
        var edition = _db.ChapterEditions.First(e => e.ChapterId == _chapter.Id && e.LanguageCode == "en");
        edition.SubmitTranslation(_lettererId);
        _db.SaveChanges();

        var handler = new ReviewEditionCommandHandler(_db);
        var result = await handler.Handle(new ReviewEditionCommand(_chapter.Id, "en", _editorId, ReviewEditionAction.Approve), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(EditionStatus.EditorApproved);
    }

    [Fact]
    public async Task Review_WhenEditorRequestsChange_TransitionsToDraftAndCreatesChangeRequest()
    {
        var edition = _db.ChapterEditions.First(e => e.ChapterId == _chapter.Id && e.LanguageCode == "en");
        edition.SubmitTranslation(_lettererId);
        var assignment = Assignment.Create(_series.Id, _chapter.Id, WorkflowEntityType.Chapter, _chapter.Id,
            Guid.NewGuid(), _lettererId, WorkspaceRole.Letterer, "Chèn chữ");
        _db.Assignments.Add(assignment);
        _db.SaveChanges();

        var handler = new ReviewEditionCommandHandler(_db);
        var result = await handler.Handle(new ReviewEditionCommand(_chapter.Id, "en", _editorId, ReviewEditionAction.RequestChange, "Dịch sai ngữ cảnh"), default);

        result.IsSuccess.Should().BeTrue();
        _db.ChangeRequests.Single(c => c.LanguageCode == "en").AssignmentId.Should().Be(assignment.Id);
        result.Value.Status.Should().Be(EditionStatus.Draft);

        var cr = _db.ChangeRequests.FirstOrDefault(c => c.ChapterId == _chapter.Id && c.LanguageCode == "en");
        cr.Should().NotBeNull();
        cr!.Description.Should().Contain("Dịch sai ngữ cảnh");
    }

    [Fact]
    public async Task Review_RequestChange_WithoutTranslatorAssignment_ReturnsConflictAndKeepsInReview()
    {
        var edition = _db.ChapterEditions.First(e => e.ChapterId == _chapter.Id && e.LanguageCode == "en");
        edition.SubmitTranslation(_lettererId);
        _db.SaveChanges();

        var handler = new ReviewEditionCommandHandler(_db);
        var result = await handler.Handle(new ReviewEditionCommand(_chapter.Id, "en", _editorId, ReviewEditionAction.RequestChange, "Sai"), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        edition.Status.Should().Be(EditionStatus.InReview);
    }

    [Fact]
    public async Task Approve_WhenNotEditorApproved_ReturnsConflict()
    {
        var handler = new ApproveEditionCommandHandler(_db, _pages.Object);
        var result = await handler.Handle(new ApproveEditionCommand(_chapter.Id, "en", _producerId), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task Approve_WhenEditorApproved_CreatesSnapshotAndSetsApproved()
    {
        // Approve chapter first
        await new ApproveChapterCommandHandler(_db, _pages.Object).Handle(new ApproveChapterCommand(_chapter.Id, _producerId), default);

        var edition = _db.ChapterEditions.First(e => e.ChapterId == _chapter.Id && e.LanguageCode == "en");
        edition.SubmitTranslation(_lettererId);
        edition.ReviewApprove();
        _db.SaveChanges();

        var handler = new ApproveEditionCommandHandler(_db, _pages.Object);
        var result = await handler.Handle(new ApproveEditionCommand(_chapter.Id, "en", _producerId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(EditionStatus.Approved);
        result.Value.CurrentSnapshotId.Should().NotBeNull();
    }

    [Fact]
    public async Task Publish_WhenApproved_SetsPublished()
    {
        var edition = _db.ChapterEditions.First(e => e.ChapterId == _chapter.Id && e.LanguageCode == "en");
        // Mark chapter approved
        await new ApproveChapterCommandHandler(_db, _pages.Object).Handle(new ApproveChapterCommand(_chapter.Id, _producerId), default);
        edition.Approve(Guid.NewGuid(), _producerId);
        edition.SetMonetization(isPaid: false, priceVnd: 0m, freePreviewPages: 0, configuredById: _producerId); // D4
        _db.SaveChanges();

        var handler = new PublishEditionCommandHandler(_db);
        var result = await handler.Handle(new PublishEditionCommand(_chapter.Id, "en", _producerId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(EditionStatus.Published);
    }

    [Fact]
    public async Task Publish_OriginalLanguageBeforeChapterRelease_ReturnsConflict_ChapterStaysApproved()
    {
        await new ApproveChapterCommandHandler(_db, _pages.Object).Handle(new ApproveChapterCommand(_chapter.Id, _producerId), default);
        var vi = ChapterEdition.Create(_chapter.Id, "vi", EditionStatus.Approved);
        vi.SetMonetization(isPaid: false, priceVnd: 0m, freePreviewPages: 0, configuredById: _producerId);
        _db.ChapterEditions.Add(vi);
        _db.SaveChanges();

        var result = await new PublishEditionCommandHandler(_db)
            .Handle(new PublishEditionCommand(_chapter.Id, "vi", _producerId), default);

        // Bản gốc phải đi qua "Phát hành chương" để chương lên Catalog, không được phát hành riêng (BR-24, BR-34)
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Phát hành chương");
        vi.Status.Should().Be(EditionStatus.Approved);
        _chapter.Status.Should().Be(ChapterStatus.ChapterApproved);
    }

    [Fact]
    public async Task Reopen_WhenReasonProvided_TransitionsToDraft()
    {
        var edition = _db.ChapterEditions.First(e => e.ChapterId == _chapter.Id && e.LanguageCode == "en");
        edition.Approve(Guid.NewGuid(), _producerId);
        _db.SaveChanges();

        var handler = new ReopenEditionCommandHandler(_db);
        var result = await handler.Handle(new ReopenEditionCommand(_chapter.Id, "en", _producerId, "Cần sửa lại dịch trang 1"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(EditionStatus.Draft);
    }

    [Fact]
    public void Domain_Reopen_WhenPublished_ShouldThrow_AndPreserveSnapshot()
    {
        var snapshotId = Guid.NewGuid();
        var edition = ChapterEdition.Create(_chapter.Id, "en");
        edition.Approve(snapshotId, _producerId);
        edition.Publish();

        var act = () => edition.Reopen();

        act.Should().Throw<InvalidOperationException>();
        edition.Status.Should().Be(EditionStatus.Published);
        edition.CurrentSnapshotId.Should().Be(snapshotId);
    }

    [Fact]
    public async Task Reopen_WhenPublished_ReturnsConflict_AndPreservesSnapshot()
    {
        var snapshotId = Guid.NewGuid();
        var edition = _db.ChapterEditions.First(e => e.ChapterId == _chapter.Id && e.LanguageCode == "en");
        edition.Approve(snapshotId, _producerId);
        edition.Publish();
        await _db.SaveChangesAsync();

        var handler = new ReopenEditionCommandHandler(_db);
        var result = await handler.Handle(new ReopenEditionCommand(_chapter.Id, "en", _producerId, "Muốn sửa lại"), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        edition.Status.Should().Be(EditionStatus.Published);
        edition.CurrentSnapshotId.Should().Be(snapshotId);
        _db.ChapterReviews.Should().BeEmpty();
    }

    [Theory]
    [InlineData(EditionStatus.InReview)]
    [InlineData(EditionStatus.EditorApproved)]
    [InlineData(EditionStatus.Approved)]
    public async Task Reopen_PrePublishedStatuses_StillTransitionsToDraft(EditionStatus initialStatus)
    {
        var edition = _db.ChapterEditions.First(e => e.ChapterId == _chapter.Id && e.LanguageCode == "en");
        if (initialStatus >= EditionStatus.InReview) edition.SubmitTranslation(_lettererId);
        if (initialStatus >= EditionStatus.EditorApproved) edition.ReviewApprove();
        if (initialStatus >= EditionStatus.Approved) edition.Approve(Guid.NewGuid(), _producerId);
        await _db.SaveChangesAsync();

        var handler = new ReopenEditionCommandHandler(_db);
        var result = await handler.Handle(new ReopenEditionCommand(_chapter.Id, "en", _producerId, "Hiệu đính"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(EditionStatus.Draft);
    }
}
