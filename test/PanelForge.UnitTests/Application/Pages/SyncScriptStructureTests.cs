using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Pages.Commands.SyncScriptStructure;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Pages;

/// <summary>Lưu khung Trang → Panel → dòng kịch bản một lần, không xoá Page/Panel có sẵn.</summary>
public class SyncScriptStructureTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IPageRepository> _pages = new();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Chapter _chapter;
    private readonly ScriptLine _action;
    private readonly ScriptLine _dialogue;

    public SyncScriptStructureTests()
    {
        var studio = StudioWorkspace.Create("Studio", Guid.NewGuid());
        _db.StudioWorkspaces.Add(studio);
        var series = DomainSeries.Create(studio.Id, "Lantern", ReadingDirection.RightToLeft);
        _db.Series.Add(series);
        _chapter = Chapter.Create(series.Id, 1, "Lá thư");
        _db.Chapters.Add(_chapter);
        var scene = Scene.Create(_chapter.Id, 1, "Bến cảng");
        _db.Scenes.Add(scene);
        _action = ScriptLine.Create(scene.Id, 1, stageDirection: "Minh chạy ra bến cảng.");
        _dialogue = ScriptLine.Create(scene.Id, 2, dialogueText: "MINH: \"Đợi đã!\"");
        _db.ScriptLines.AddRange(_action, _dialogue);
        _db.SaveChanges();

        _auth.Setup(a => a.GetMemberRoleAsync(_writerId, studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
    }

    private Task<Result<SyncScriptStructureResult>> Sync(params ScriptStructurePage[] pages) =>
        new SyncScriptStructureCommandHandler(_db, _pages.Object, new PassThroughUnitOfWork(), _auth.Object)
            .Handle(new SyncScriptStructureCommand(_chapter.Id, _writerId, pages), default);

    private static ScriptStructurePage Page(int number, params ScriptStructurePanel[] panels) => new(number, panels);
    private static ScriptStructurePanel Panel(int number, params Guid[] lines) => new(number, lines);

    [Fact]
    public async Task CreatesMissingPagesAndPanels_AndAnchorsLines()
    {
        var result = await Sync(Page(1, Panel(1, _action.Id), Panel(2, _dialogue.Id)));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.PagesCreated.Should().Be(1);
        result.Value.PanelsCreated.Should().Be(2);

        _db.ChangeTracker.Clear();
        var panels = _db.Panels.ToDictionary(p => p.PanelNumber, p => p.Id);
        _db.ScriptLines.Single(l => l.Id == _action.Id).PanelId.Should().Be(panels[1]);
        _db.ScriptLines.Single(l => l.Id == _dialogue.Id).PanelId.Should().Be(panels[2]);
        _pages.Verify(r => r.SaveAsync(It.IsAny<Page>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SecondSync_ReusesPanels_MovesLine_AndReportsUnlistedPages()
    {
        await Sync(Page(1, Panel(1, _action.Id, _dialogue.Id)), Page(2, Panel(1)));

        var result = await Sync(Page(1, Panel(1, _action.Id), Panel(2, _dialogue.Id)));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.PagesCreated.Should().Be(0);
        result.Value.PanelsCreated.Should().Be(1);
        result.Value.UnlistedPageNumbers.Should().Equal(2);
        _db.Pages.Count().Should().Be(2, "trang không còn trong kịch bản không bị xoá");
    }

    [Fact]
    public async Task Writer_IsBlocked_AfterScriptIsFrozen()
    {
        _chapter.FreezeScript(Guid.NewGuid());
        await _db.SaveChangesAsync();

        var result = await Sync(Page(1, Panel(1, _action.Id)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _db.Pages.Should().BeEmpty();
    }

    [Fact]
    public async Task RejectsLineAnchoredTwice()
    {
        var result = await Sync(Page(1, Panel(1, _action.Id), Panel(2, _action.Id)));

        result.IsSuccess.Should().BeFalse();
        _db.Pages.Should().BeEmpty();
    }
}
