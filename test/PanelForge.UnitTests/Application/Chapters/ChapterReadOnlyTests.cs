using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.CanvasElements.Commands.AcknowledgeOverflow;
using PanelForge.Application.Features.CanvasElements.Commands.AddElement;
using PanelForge.Application.Features.CanvasElements.Commands.MoveElement;
using PanelForge.Application.Features.CanvasElements.Commands.RemoveElement;
using PanelForge.Application.Features.CanvasElements.Commands.SetReadingOrder;
using PanelForge.Application.Features.CanvasElements.Commands.UpdateElementText;
using PanelForge.Application.Features.Chapters.Commands.DeleteChapter;
using PanelForge.Application.Features.Chapters.Commands.ReopenChapter;
using PanelForge.Application.Features.Chapters.Commands.UpdateChapter;
using PanelForge.Application.Features.Pages.Commands.CreatePage;
using PanelForge.Application.Features.Pages.Commands.DeletePage;
using PanelForge.Application.Features.Pages.Commands.ReorderPage;
using PanelForge.Application.Features.Pages.Commands.UpdateCanvasSettings;
using PanelForge.Application.Features.Panels.Commands.AssignPanelScene;
using PanelForge.Application.Features.Panels.Commands.AssignPanelScriptLines;
using PanelForge.Application.Features.Panels.Commands.CreatePanel;
using PanelForge.Application.Features.Panels.Commands.DeletePanel;
using PanelForge.Application.Features.Panels.Commands.UpdatePanelCoordinates;
using PanelForge.Application.Features.Scenes.Commands.CreateScene;
using PanelForge.Application.Features.Scenes.Commands.DeleteScene;
using PanelForge.Application.Features.Scenes.Commands.ReorderScenes;
using PanelForge.Application.Features.Scenes.Commands.UpdateScene;
using PanelForge.Application.Features.Scenes.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Chapters;

/// <summary>BR-12 / CF5 bước 2: chương Approved/Published chỉ đọc với mọi lệnh sửa nội dung (409); mở lại thì sửa được.</summary>
public class ChapterReadOnlyTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IPageRepository> _pages = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Chapter _chapter;
    private readonly Scene _scene;
    private readonly Page _page;
    private readonly Panel _panel;

    public ChapterReadOnlyTests()
    {
        var studio = StudioWorkspace.Create("Studio", _userId);
        _db.StudioWorkspaces.Add(studio);
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(studio.Id, _userId, WorkspaceRole.Producer));
        var series = DomainSeries.Create(studio.Id, "Lantern", ReadingDirection.RightToLeft);
        _db.Series.Add(series);
        _chapter = Chapter.Create(series.Id, 1, "Lá thư");
        _chapter.Approve();
        _db.Chapters.Add(_chapter);
        _scene = Scene.Create(_chapter.Id, 1, "Bến cảng");
        _db.Scenes.Add(_scene);
        _page = Page.CreateNew(_chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        _db.Pages.Add(_page);
        _panel = Panel.Create(_page.Id, 1, 1);
        _db.Panels.Add(_panel);
        _db.SaveChanges();
        _pages.Setup(r => r.GetAsync(_page.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_page);
    }

    private static void ShouldBeReadOnly<T>(Result<T> result) =>
        result.Should().Match<Result<T>>(r => !r.IsSuccess && r.ErrorCode == ResultErrorCodes.ChapterReadOnly && r.ErrorMessage!.Contains("chỉ đọc"));

    [Fact]
    public async Task ChapterAndSceneCommands_OnApprovedChapter_AreConflict()
    {
        ShouldBeReadOnly(await new UpdateChapterCommandHandler(_db).Handle(
            new UpdateChapterCommand(_chapter.Id, _userId, "Tên mới", null, false), default));
        ShouldBeReadOnly(await new DeleteChapterCommandHandler(_db).Handle(new DeleteChapterCommand(_chapter.Id, _userId), default));
        ShouldBeReadOnly(await new CreateSceneCommandHandler(_db).Handle(new CreateSceneCommand(_chapter.Id, _userId), default));
        ShouldBeReadOnly(await new UpdateSceneCommandHandler(_db).Handle(new UpdateSceneCommand(_scene.Id, _userId, "x", null), default));
        ShouldBeReadOnly(await new ReorderScenesCommandHandler(_db).Handle(
            new ReorderScenesCommand(_chapter.Id, _userId, [new ReorderSceneItem(_scene.Id, 2)]), default));
        ShouldBeReadOnly(await new DeleteSceneCommandHandler(_db).Handle(new DeleteSceneCommand(_scene.Id, _userId), default));

        _db.ChangeTracker.Clear();
        _db.Chapters.Single().Title.Should().Be("Lá thư");
        _db.Scenes.Single().Heading.Should().Be("Bến cảng");
    }

    [Fact]
    public async Task PanelAndPageCommands_OnApprovedChapter_AreConflict()
    {
        ShouldBeReadOnly(await new CreatePanelCommandHandler(_db).Handle(new CreatePanelCommand(_page.Id, _userId), default));
        ShouldBeReadOnly(await new UpdatePanelCoordinatesCommandHandler(_db).Handle(
            new UpdatePanelCoordinatesCommand(_panel.Id, _userId, "[]"), default));
        ShouldBeReadOnly(await new AssignPanelSceneCommandHandler(_db).Handle(new AssignPanelSceneCommand(_panel.Id, _scene.Id, _userId), default));
        ShouldBeReadOnly(await new AssignPanelScriptLinesCommandHandler(_db).Handle(
            new AssignPanelScriptLinesCommand(_panel.Id, [], _userId), default));
        ShouldBeReadOnly(await new DeletePanelCommandHandler(_db).Handle(new DeletePanelCommand(_panel.Id, _userId), default));

        var uow = new PassThroughUnitOfWork();
        ShouldBeReadOnly(await new CreatePageCommandHandler(_db, _pages.Object, uow).Handle(new CreatePageCommand(_chapter.Id), default));
        ShouldBeReadOnly(await new ReorderPageCommandHandler(_db, _pages.Object, uow).Handle(new ReorderPageCommand(_page.Id, 2, _userId), default));
        ShouldBeReadOnly(await new UpdateCanvasSettingsCommandHandler(_pages.Object, _db, uow).Handle(
            new UpdateCanvasSettingsCommand(_page.Id, 1000, 1500, 300, "StandardPage"), default));
        ShouldBeReadOnly(await new DeletePageCommandHandler(_db, _pages.Object, uow, Mock.Of<ICurrentUserService>()).Handle(
            new DeletePageCommand(_page.Id), default));

        _pages.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
        _db.ChangeTracker.Clear();
        _db.Panels.Should().ContainSingle();
    }

    [Fact]
    public async Task ElementCommands_OnApprovedChapter_AreConflict_AndNothingIsAppended()
    {
        var elementId = Guid.NewGuid();
        ShouldBeReadOnly(await new AddElementCommandHandler(_pages.Object, _db).Handle(
            new AddElementCommand(_page.Id, "NarrationBox", 0, 0, 10, 10, 1, "x", null, _userId), default));
        ShouldBeReadOnly(await new MoveElementCommandHandler(_pages.Object, _db).Handle(
            new MoveElementCommand(_page.Id, elementId, 1, 1, 10, 10, null, _userId, 1), default));
        ShouldBeReadOnly(await new RemoveElementCommandHandler(_pages.Object, _db).Handle(
            new RemoveElementCommand(_page.Id, elementId, "USER_DELETE", _userId), default));
        ShouldBeReadOnly(await new SetReadingOrderCommandHandler(_pages.Object, _db).Handle(
            new SetReadingOrderCommand(_page.Id, elementId, 1, _userId), default));
        ShouldBeReadOnly(await new AcknowledgeOverflowCommandHandler(_pages.Object, _db).Handle(
            new AcknowledgeOverflowCommand(_page.Id, elementId, "ok", _userId), default));
        ShouldBeReadOnly(await new UpdateElementTextCommandHandler(_pages.Object, _db).Handle(
            new UpdateElementTextCommand(_page.Id, elementId, "x", null, _userId), default));

        _pages.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AfterReopen_ContentIsEditableAgain()
    {
        (await new ReopenChapterCommandHandler(_db).Handle(
            new ReopenChapterCommand(_chapter.Id, _userId, "Sửa theo phản hồi"), default)).IsSuccess.Should().BeTrue();

        var updated = await new UpdateSceneCommandHandler(_db).Handle(new UpdateSceneCommand(_scene.Id, _userId, "Bến cảng đêm", null), default);

        updated.IsSuccess.Should().BeTrue(updated.ErrorMessage);
        updated.Value!.Heading.Should().Be("Bến cảng đêm");
    }
}
