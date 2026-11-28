using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.PageComments.Commands.ConvertPageCommentToRework;
using PanelForge.Application.Features.PageComments.Commands.CreatePageComment;
using PanelForge.Application.Features.PageComments.Commands.ReopenPageComment;
using PanelForge.Application.Features.PageComments.Commands.ResolvePageComment;
using PanelForge.Application.Features.PageComments.Queries.GetSeriesPageComments;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Preview;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.PageComments;

public class PageCommentTests
{
    // ── Entity Invariants ──

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyMessage_ThrowsArgumentException(string message)
    {
        var act = () => PageComment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), message);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithTooLongMessage_ThrowsArgumentException()
    {
        var tooLong = new string('a', PageComment.MaxMessageLength + 1);
        var act = () => PageComment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tooLong);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0.5, null)]
    [InlineData(null, 0.5)]
    public void Create_WithMismatchedPinPair_ThrowsArgumentException(double? pinX, double? pinY)
    {
        var act = () => PageComment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fix face", pinX: pinX, pinY: pinY);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-0.01, 0.5)]
    [InlineData(1.01, 0.5)]
    [InlineData(0.5, -0.01)]
    [InlineData(0.5, 1.01)]
    [InlineData(double.NaN, 0.5)]
    public void Create_WithOutOfRangePin_ThrowsArgumentOutOfRangeException(double pinX, double pinY)
    {
        var act = () => PageComment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fix line", pinX: pinX, pinY: pinY);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void StateTransitions_InvalidTransitions_ThrowInvalidOperationException()
    {
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var openComment = PageComment.Create(Guid.NewGuid(), Guid.NewGuid(), userId, "  Fix hand  ", pinX: 0d, pinY: 1d);
        openComment.Message.Should().Be("Fix hand");
        openComment.Status.Should().Be(PageCommentStatus.Open);

        // Cannot reopen when Open
        openComment.Invoking(c => c.Reopen(userId)).Should().Throw<InvalidOperationException>();

        // Resolve -> Resolved
        openComment.Resolve(userId, "  Done  ");
        openComment.Status.Should().Be(PageCommentStatus.Resolved);
        openComment.ResolutionNote.Should().Be("Done");

        // Cannot resolve again or convert to rework when Resolved
        openComment.Invoking(c => c.Resolve(userId)).Should().Throw<InvalidOperationException>();
        openComment.Invoking(c => c.MarkConvertedToRework(taskId, userId)).Should().Throw<InvalidOperationException>();

        // Reopen -> Open
        openComment.Reopen(userId);
        openComment.Status.Should().Be(PageCommentStatus.Open);
        openComment.ResolutionNote.Should().BeNull();

        // MarkConvertedToRework -> ConvertedToRework
        openComment.MarkConvertedToRework(taskId, userId);
        openComment.Status.Should().Be(PageCommentStatus.ConvertedToRework);
        openComment.StageTaskId.Should().Be(taskId);

        // Cannot resolve, reopen, or convert again
        openComment.Invoking(c => c.Resolve(userId)).Should().Throw<InvalidOperationException>();
        openComment.Invoking(c => c.Reopen(userId)).Should().Throw<InvalidOperationException>();
        openComment.Invoking(c => c.MarkConvertedToRework(Guid.NewGuid(), userId)).Should().Throw<InvalidOperationException>();
    }

    // ── Handler Tests ──

    private sealed class Fixture
    {
        public readonly Guid WorkspaceId = Guid.NewGuid();
        public readonly User Producer;
        public readonly User Artist;
        public readonly Series Series;
        public readonly Chapter Chapter;
        public readonly Chapter OtherChapter;
        public readonly Page Page1;
        public readonly Page Page2;
        public readonly Page OtherChapterPage;
        public readonly Panel PanelOnPage1;
        public readonly Panel PanelOnOtherChapter;
        public readonly PipelineStage Pencil;
        public readonly PipelineStage Ink;
        public readonly List<PageComment> Comments = [];
        public readonly List<StageTask> Tasks = [];
        public readonly List<StageTaskLot> Lots = [];
        public readonly List<StageTaskSubmission> Submissions = [];
        public readonly List<Notification> Notifications = [];
        public readonly Mock<IPanelForgeDbContext> Db = new();

        public Fixture(ChapterStatus chapterStatus = ChapterStatus.InProduction)
        {
            Producer = User.Create("producer@studio.test", "Studio Producer");
            Artist = User.Create("artist@studio.test", "Lead Artist");
            Series = Series.Create(WorkspaceId, "Test Manga", ReadingDirection.RightToLeft);

            Chapter = Chapter.Create(Series.Id, 1, "Chapter 1");
            SetChapterStatus(Chapter, chapterStatus);

            OtherChapter = Chapter.Create(Series.Id, 2, "Chapter 2");

            Page1 = Page.CreateNew(Chapter.Id, 1, default, 1200, 1800, 300);
            Page2 = Page.CreateNew(Chapter.Id, 2, default, 1200, 1800, 300);
            OtherChapterPage = Page.CreateNew(OtherChapter.Id, 1, default, 1200, 1800, 300);

            PanelOnPage1 = Panel.Create(Page1.Id, 1, 1, "{\"x\":0,\"y\":0,\"width\":100,\"height\":100}");
            PanelOnOtherChapter = Panel.Create(OtherChapterPage.Id, 1, 1, "{\"x\":0,\"y\":0,\"width\":100,\"height\":100}");

            var def = PipelineDefinition.Create(WorkspaceId, "Standard", seriesId: Series.Id);
            Pencil = PipelineStage.Create(def.Id, "Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true, isApprovalGate: true);
            Ink = PipelineStage.Create(def.Id, "Ink", "ink", 2, allowedRole: WorkspaceRole.Artist, isApprovalGate: true, isTerminal: true);

            Db.Setup(d => d.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { Series }));
            Db.Setup(d => d.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter> { Chapter, OtherChapter }));
            Db.Setup(d => d.Pages).Returns(DbSetMockHelper.CreateDbSetMock(new List<Page> { Page1, Page2, OtherChapterPage }));
            Db.Setup(d => d.Panels).Returns(DbSetMockHelper.CreateDbSetMock(new List<Panel> { PanelOnPage1, PanelOnOtherChapter }));
            Db.Setup(d => d.Users).Returns(DbSetMockHelper.CreateDbSetMock(new List<User> { Producer, Artist }));
            Db.Setup(d => d.PipelineDefinitions).Returns(DbSetMockHelper.CreateDbSetMock(new List<PipelineDefinition> { def }));
            Db.Setup(d => d.PipelineStages).Returns(DbSetMockHelper.CreateDbSetMock(new List<PipelineStage> { Pencil, Ink }));
            Db.Setup(d => d.PageComments).Returns(DbSetMockHelper.CreateDbSetMock(Comments));
            Db.Setup(d => d.StageTasks).Returns(DbSetMockHelper.CreateDbSetMock(Tasks));
            Db.Setup(d => d.StageTaskLots).Returns(DbSetMockHelper.CreateDbSetMock(Lots));
            Db.Setup(d => d.StageTaskSubmissions).Returns(DbSetMockHelper.CreateDbSetMock(Submissions));
            Db.Setup(d => d.Notifications).Returns(DbSetMockHelper.CreateDbSetMock(Notifications));
            Db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Db.Setup(d => d.LockChapterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        private static void SetChapterStatus(Chapter chapter, ChapterStatus status)
        {
            var prop = typeof(Chapter).GetProperty(nameof(Chapter.Status))!;
            prop.SetValue(chapter, status);
        }

        public StageTask AddTask(PipelineStage stage, Guid? pageId, StageTaskStatus status, Guid? assigneeUserId = null)
        {
            var task = StageTask.Create(Series.Id, Chapter.Id, stage.Id, pageId);
            if (assigneeUserId.HasValue)
            {
                var lot = StageTaskLot.Create(Series.Id, Chapter.Id, stage.Id, assigneeUserId.Value, WorkspaceRole.Artist, Producer.Id, null, null);
                Lots.Add(lot);
                task.AssignTo(lot.Id, prerequisitesApproved: true);

                if (status is StageTaskStatus.InProgress or StageTaskStatus.Submitted or StageTaskStatus.UnderReview or StageTaskStatus.Approved)
                    task.Start(prerequisitesApproved: true);

                if (status is StageTaskStatus.Submitted or StageTaskStatus.UnderReview or StageTaskStatus.Approved)
                {
                    var sub = StageTaskSubmission.Create(task.Id, Chapter.Id, assigneeUserId.Value, 1, null, autoApproved: false);
                    Submissions.Add(sub);
                    task.Submit(sub.Id, isApprovalGate: true);
                }

                if (status is StageTaskStatus.UnderReview or StageTaskStatus.Approved)
                    task.OpenReview(Producer.Id);

                if (status == StageTaskStatus.Approved)
                    task.Approve();
            }

            Tasks.Add(task);
            return task;
        }
    }

    [Fact]
    public async Task CreateHandler_WhenPageNotInChapter_ReturnsNotFound()
    {
        var fx = new Fixture();
        var handler = new CreatePageCommentCommandHandler(fx.Db.Object);

        var result = await handler.Handle(
            new CreatePageCommentCommand(fx.Chapter.Id, fx.Producer.Id, fx.OtherChapterPage.Id, null, null, 0.2, 0.3, "Wrong page"),
            default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task CreateHandler_WhenPanelNotInChapter_ReturnsNotFound()
    {
        var fx = new Fixture();
        var handler = new CreatePageCommentCommandHandler(fx.Db.Object);

        var result = await handler.Handle(
            new CreatePageCommentCommand(fx.Chapter.Id, fx.Producer.Id, null, fx.PanelOnOtherChapter.Id, null, null, null, "Wrong panel"),
            default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task CreateHandler_WhenChapterApproved_StillAllowsCommentAndReturnsEnrichedDto()
    {
        var fx = new Fixture(ChapterStatus.ChapterApproved);
        var handler = new CreatePageCommentCommandHandler(fx.Db.Object);

        var result = await handler.Handle(
            new CreatePageCommentCommand(fx.Chapter.Id, fx.Producer.Id, fx.Page1.Id, fx.PanelOnPage1.Id, null, 0.4, 0.6, "Fix eye symmetry"),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AuthorName.Should().Be("Studio Producer");
        result.Value.ChapterNumber.Should().Be(1);
        result.Value.PageNumber.Should().Be(1);
        result.Value.PanelNumber.Should().Be(1);
        result.Value.PinX.Should().Be(0.4);
        result.Value.PinY.Should().Be(0.6);
        result.Value.Status.Should().Be(PageCommentStatus.Open);
        fx.Comments.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReworkHandler_WhenChapterNotInProduction_ReturnsConflict()
    {
        var fx = new Fixture(ChapterStatus.ChapterApproved);
        var task = fx.AddTask(fx.Pencil, fx.Page1.Id, StageTaskStatus.Approved, fx.Artist.Id);
        var comment = PageComment.Create(fx.Series.Id, fx.Chapter.Id, fx.Producer.Id, "Needs fix", fx.Page1.Id);
        fx.Comments.Add(comment);

        var handler = new ConvertPageCommentToReworkCommandHandler(fx.Db.Object, Mock.Of<IPageRepository>());
        var result = await handler.Handle(
            new ConvertPageCommentToReworkCommand(fx.Chapter.Id, comment.Id, fx.Producer.Id, task.Id, "Please redraw"),
            default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        comment.Status.Should().Be(PageCommentStatus.Open);
    }

    [Fact]
    public async Task ReworkHandler_WhenTaskApproved_ReopensTaskMarksDownstreamOutdatedAndConvertsComment()
    {
        var fx = new Fixture(ChapterStatus.InProduction);
        var pencilTask = fx.AddTask(fx.Pencil, fx.Page1.Id, StageTaskStatus.Approved, fx.Artist.Id);
        var inkTask = fx.AddTask(fx.Ink, fx.Page1.Id, StageTaskStatus.InProgress, fx.Artist.Id);
        var comment = PageComment.Create(fx.Series.Id, fx.Chapter.Id, fx.Producer.Id, "Fix pose", fx.Page1.Id);
        fx.Comments.Add(comment);

        var handler = new ConvertPageCommentToReworkCommandHandler(fx.Db.Object, Mock.Of<IPageRepository>());
        var result = await handler.Handle(
            new ConvertPageCommentToReworkCommand(fx.Chapter.Id, comment.Id, fx.Producer.Id, pencilTask.Id, "Fix hand anatomy"),
            default);

        result.IsSuccess.Should().BeTrue();
        pencilTask.Status.Should().Be(StageTaskStatus.ReworkRequired);
        pencilTask.ReopenReason.Should().Be("Fix hand anatomy");
        inkTask.IsInputOutdated.Should().BeTrue();
        comment.Status.Should().Be(PageCommentStatus.ConvertedToRework);
        comment.StageTaskId.Should().Be(pencilTask.Id);
        result.Value!.StageName.Should().Be("Pencil");
    }

    [Fact]
    public async Task ReworkHandler_WhenTaskSubmitted_RequestsChangesAndConvertsComment()
    {
        var fx = new Fixture(ChapterStatus.InProduction);
        var pencilTask = fx.AddTask(fx.Pencil, fx.Page1.Id, StageTaskStatus.Submitted, fx.Artist.Id);
        var comment = PageComment.Create(fx.Series.Id, fx.Chapter.Id, fx.Producer.Id, "Too dark", fx.Page1.Id);
        fx.Comments.Add(comment);

        var handler = new ConvertPageCommentToReworkCommandHandler(fx.Db.Object, Mock.Of<IPageRepository>());
        var result = await handler.Handle(
            new ConvertPageCommentToReworkCommand(fx.Chapter.Id, comment.Id, fx.Producer.Id, pencilTask.Id, "Lighten shadows"),
            default);

        result.IsSuccess.Should().BeTrue();
        pencilTask.Status.Should().Be(StageTaskStatus.ReworkRequired);
        comment.Status.Should().Be(PageCommentStatus.ConvertedToRework);
        comment.StageTaskId.Should().Be(pencilTask.Id);
        fx.Submissions.Single().Status.Should().Be(SubmissionStatus.Rejected);
    }

    [Fact]
    public async Task ReworkHandler_WhenCommentAlreadyConverted_ReturnsConflict()
    {
        var fx = new Fixture(ChapterStatus.InProduction);
        var pencilTask = fx.AddTask(fx.Pencil, fx.Page1.Id, StageTaskStatus.Approved, fx.Artist.Id);
        var comment = PageComment.Create(fx.Series.Id, fx.Chapter.Id, fx.Producer.Id, "Fix pose", fx.Page1.Id);
        comment.MarkConvertedToRework(pencilTask.Id, fx.Producer.Id);
        fx.Comments.Add(comment);

        var handler = new ConvertPageCommentToReworkCommandHandler(fx.Db.Object, Mock.Of<IPageRepository>());
        var result = await handler.Handle(
            new ConvertPageCommentToReworkCommand(fx.Chapter.Id, comment.Id, fx.Producer.Id, pencilTask.Id, "Again"),
            default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task ReworkHandler_WhenTaskNotOnCommentPage_ReturnsValidation()
    {
        var fx = new Fixture(ChapterStatus.InProduction);
        var page2Task = fx.AddTask(fx.Pencil, fx.Page2.Id, StageTaskStatus.Approved, fx.Artist.Id);
        var commentOnPage1 = PageComment.Create(fx.Series.Id, fx.Chapter.Id, fx.Producer.Id, "Page 1 issue", fx.Page1.Id);
        fx.Comments.Add(commentOnPage1);

        var handler = new ConvertPageCommentToReworkCommandHandler(fx.Db.Object, Mock.Of<IPageRepository>());
        var result = await handler.Handle(
            new ConvertPageCommentToReworkCommand(fx.Chapter.Id, commentOnPage1.Id, fx.Producer.Id, page2Task.Id, "Fix"),
            default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task ListResolveReopenHandlers_WorkEndToEnd()
    {
        var fx = new Fixture();
        var c1 = PageComment.Create(fx.Series.Id, fx.Chapter.Id, fx.Producer.Id, "First", fx.Page1.Id);
        fx.Comments.Add(c1);

        var listResult = await new GetSeriesPageCommentsQueryHandler(fx.Db.Object)
            .Handle(new GetSeriesPageCommentsQuery(fx.Series.Id, fx.Chapter.Id, PageCommentStatus.Open), default);
        listResult.IsSuccess.Should().BeTrue();
        listResult.Value.Should().HaveCount(1);

        var resolveResult = await new ResolvePageCommentCommandHandler(fx.Db.Object)
            .Handle(new ResolvePageCommentCommand(fx.Chapter.Id, c1.Id, fx.Producer.Id, "Fixed offline"), default);
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value!.Status.Should().Be(PageCommentStatus.Resolved);
        resolveResult.Value.ResolutionNote.Should().Be("Fixed offline");

        var reopenResult = await new ReopenPageCommentCommandHandler(fx.Db.Object)
            .Handle(new ReopenPageCommentCommand(fx.Chapter.Id, c1.Id, fx.Producer.Id), default);
        reopenResult.IsSuccess.Should().BeTrue();
        reopenResult.Value!.Status.Should().Be(PageCommentStatus.Open);
    }
}
