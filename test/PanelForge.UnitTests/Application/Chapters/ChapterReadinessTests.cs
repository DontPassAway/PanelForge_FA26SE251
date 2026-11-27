using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Commands.FreezeScript;
using PanelForge.Application.Features.Chapters.Queries.GetChapterById;
using PanelForge.Application.Features.Chapters.Queries.GetChapterReadiness;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Chapters;

public class ChapterReadinessTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly ChapterCastInheritanceResolver _resolver;
    private readonly ChapterCastService _castService;
    private readonly ChapterCastGate _castGate;

    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly SeriesBible _bible;
    private readonly PipelineDefinition _pipeline;

    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly Guid _editorId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();

    public ChapterReadinessTests()
    {
        _resolver = new ChapterCastInheritanceResolver(_db);
        _castService = new ChapterCastService(_db, _resolver);
        _castGate = new ChapterCastGate(_db, _castService, _resolver);

        _studio = StudioWorkspace.Create("Readiness Studio", _producerId);
        _db.StudioWorkspaces.Add(_studio);

        _series = DomainSeries.Create(_studio.Id, "Dragon Saga", ReadingDirection.RightToLeft);
        _db.Series.Add(_series);

        _bible = SeriesBible.Create(_series.Id, _studio.Id, "Dragon Saga Bible");
        _db.SeriesBibles.Add(_bible);

        _pipeline = PipelineDefinition.Create(_studio.Id, "Default", seriesId: _series.Id);
        var pencil = _pipeline.AddStage("Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
        var letter = _pipeline.AddStage("Letter", "letter", 2, allowedRole: WorkspaceRole.Letterer);
        _db.PipelineDefinitions.Add(_pipeline);
        _db.PipelineStages.AddRange(pencil, letter);

        _db.WorkspaceMembers.AddRange(
            WorkspaceMember.Create(_studio.Id, _writerId, WorkspaceRole.Writer),
            WorkspaceMember.Create(_studio.Id, _producerId, WorkspaceRole.Producer),
            WorkspaceMember.Create(_studio.Id, _editorId, WorkspaceRole.Editor),
            WorkspaceMember.Create(_studio.Id, _artistId, WorkspaceRole.Artist));
        _db.SaveChanges();

        _auth.Setup(a => a.GetMemberRoleAsync(_producerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Producer);
        _auth.Setup(a => a.GetSeriesMemberRoleAsync(_producerId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Producer);
        _auth.Setup(a => a.GetMemberRoleAsync(_writerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
        _auth.Setup(a => a.GetSeriesMemberRoleAsync(_writerId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
    }

    [Fact]
    public async Task GetChapterReadiness_WhenScriptDraft_BlocksStartProduction()
    {
        var chapter = Chapter.Create(_series.Id, 1, "Khởi đầu");
        var scene = Scene.Create(chapter.Id, 1, "Cảnh 1");
        var page = Page.CreateNew(chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        var panel = Panel.Create(page.Id, 1, 1);
        var line = ScriptLine.Create(scene.Id, 1, "Xin chào!", speakerLabel: "Goku");
        line.AssignToPanel(panel.Id);
        _db.Chapters.Add(chapter);
        _db.Scenes.Add(scene);
        _db.Pages.Add(page);
        _db.Panels.Add(panel);
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();

        var handler = new GetChapterReadinessQueryHandler(_db, _auth.Object, _castGate);
        var result = await handler.Handle(new GetChapterReadinessQuery(chapter.Id, _producerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Status.Should().Be("NOT_READY");
        dto.CanStartProduction.Should().BeFalse();
        dto.Blockers.Should().Contain(b => b.Code == "SCRIPT_NOT_SUBMITTED");
        dto.Script.ScriptStatus.Should().Be("Draft");
        dto.Script.PageCount.Should().Be(1);
        dto.Script.PanelCount.Should().Be(1);
    }

    [Fact]
    public async Task GetChapterReadiness_NonVisualAndCrowdEntries_DoNotBlockGate1()
    {
        var chapter = Chapter.Create(_series.Id, 1, "Sẵn sàng");
        var scene = Scene.Create(chapter.Id, 1, "Cảnh 1");
        var page = Page.CreateNew(chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        var panel = Panel.Create(page.Id, 1, 1);

        var hero = _bible.AddEntry(BibleEntryCategory.Character, "GOKU", "Goku", "Nhân vật chính");
        var heroRev = hero.AddRevision("Mặc định", "Mô tả", "https://cdn.example/goku.png");
        heroRev.Lock(_editorId);

        var crowd = _bible.AddEntry(BibleEntryCategory.Character, "CROWD", "Người qua đường", "Quần chúng");
        crowd.SetCrowd(true);

        var term = _bible.AddEntry(BibleEntryCategory.StyleRule, "RULE1", "Quy tắc Ki", "Ki không vượt giới hạn");
        term.AddRevision("Mặc định", "Mô tả quy tắc", "hash-rule-1");

        var line1 = ScriptLine.Create(scene.Id, 1, "Tới đây!", speakerLabel: "Goku");
        line1.AssignToPanel(panel.Id);
        var line2 = ScriptLine.Create(scene.Id, 2, "Ối!", speakerLabel: "Người qua đường");
        line2.AssignToPanel(panel.Id);
        var linkRule = ScriptLineBibleLink.Create(line1.Id, term.Id, "Quy tắc Ki");

        _db.Chapters.Add(chapter);
        _db.Scenes.Add(scene);
        _db.Pages.Add(page);
        _db.Panels.Add(panel);
        _db.ScriptLines.AddRange(line1, line2);
        _db.ScriptLineBibleLinks.Add(linkRule);
        await _db.SaveChangesAsync();

        // Writer submits script (Draft -> InReview)
        var submitHandler = new ScriptLifecycleCommandHandler(_db, _auth.Object, _castGate, _castService);
        var submitResult = await submitHandler.Handle(new SubmitScriptCommand(chapter.Id, _writerId, "Nộp v1"), CancellationToken.None);
        submitResult.IsSuccess.Should().BeTrue();

        // Add an unresolved comment — should be WARNING, not BLOCKING
        _db.ScriptComments.Add(ScriptComment.Create(chapter.Id, scene.Id, line1.Id, null, "Kiểm tra lại câu thoại", _producerId));
        await _db.SaveChangesAsync();

        var handler = new GetChapterReadinessQueryHandler(_db, _auth.Object, _castGate);
        var result = await handler.Handle(new GetChapterReadinessQuery(chapter.Id, _producerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.CanStartProduction.Should().BeTrue();
        dto.Status.Should().Be("READY");
        dto.Blockers.Should().BeEmpty();
        dto.Warnings.Should().Contain(w => w.Code == "UNRESOLVED_SCRIPT_COMMENTS");
        dto.Bible.CrowdOptionalCount.Should().Be(1);
        dto.Bible.RequiredTotal.Should().Be(2); // Goku + Quy tắc Ki
        dto.Bible.ReadyCount.Should().Be(2);    // Both satisfied (Goku CanonLocked, StyleRule non-visual defined)
    }

    [Fact]
    public async Task GetChapterById_ReturnsFrozenScriptVersionAndHandOffFields()
    {
        var chapter = Chapter.Create(_series.Id, 2, "Chương 2");
        var versionId = Guid.NewGuid();
        chapter.SubmitScript(_writerId, versionId);
        chapter.StartProduction(_producerId, "hash-abc", versionId);
        chapter.Approve();
        chapter.HandOff(_producerId);
        _db.Chapters.Add(chapter);
        await _db.SaveChangesAsync();

        var handler = new GetChapterByIdQueryHandler(_db);
        var result = await handler.Handle(new GetChapterByIdQuery(chapter.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FrozenScriptVersionId.Should().Be(versionId);
        result.Value!.IsHandedOff.Should().BeTrue();
        result.Value!.HandedOffById.Should().Be(_producerId);
    }
}
