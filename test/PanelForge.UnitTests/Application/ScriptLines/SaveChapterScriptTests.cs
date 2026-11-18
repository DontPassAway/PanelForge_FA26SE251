using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptLines.Commands.SaveChapterScript;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.ScriptLines;

public class SaveChapterScriptTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Mock<IPageRepository> _pages = new();
    private readonly Mock<IChapterCastGate> _castGate = new();
    private readonly ChapterCastService _castService;
    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Scene _scene;

    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();

    public SaveChapterScriptTests()
    {
        _studio = StudioWorkspace.Create("Studio SCR-14", _producerId);
        _db.StudioWorkspaces.Add(_studio);

        _series = DomainSeries.Create(_studio.Id, "Manga Test", ReadingDirection.RightToLeft);
        _db.Series.Add(_series);

        _chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(_chapter);

        _scene = Scene.Create(_chapter.Id, 1, "Cảnh mở đầu");
        _db.Scenes.Add(_scene);

        var resolver = new ChapterCastInheritanceResolver(_db);
        _castService = new ChapterCastService(_db, resolver);

        _db.SaveChanges();

        _auth.Setup(a => a.GetMemberRoleAsync(_writerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
        _auth.Setup(a => a.GetMemberRoleAsync(_producerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Producer);

        _castGate.Setup(g => g.EvaluateSubmitAsync(_chapter.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChapterCastSubmitGateDto(true, []));
    }

    private SaveChapterScriptCommandHandler CreateSaveHandler(IUnitOfWork? uow = null) =>
        new(_db, _auth.Object, _pages.Object, _castService, uow ?? new PassThroughUnitOfWork());

    private SubmitChapterScriptCommandHandler CreateSubmitHandler(IChapterCastGate? gate = null, IUnitOfWork? uow = null) =>
        new(_db, _auth.Object, _pages.Object, _castService, gate ?? _castGate.Object, uow ?? new PassThroughUnitOfWork());

    [Fact]
    public async Task SaveChapterScript_FullSave_CreatesVersion()
    {
        var body = new ChapterScriptBody(
            ExpectedRevisionId: null,
            Pages:
            [
                new ScriptTreePage(
                    Id: null,
                    Number: 1,
                    Panels:
                    [
                        new ScriptTreePanel(
                            Id: null,
                            Number: 1,
                            Direction: "Toàn cảnh góc rộng.",
                            CastRevisions: null,
                            Lines:
                            [
                                new ScriptTreeLine(null, ScriptLineKind.Action, null, "Minh bước vào quán trọ.")
                            ])
                    ])
            ]);

        var handler = CreateSaveHandler();
        var result = await handler.Handle(new SaveChapterScriptCommand(_chapter.Id, _writerId, body), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Created.Should().BeTrue();
        result.Value.VersionNumber.Should().Be(1);
        result.Value.RevisionId.Should().NotBeEmpty();
        result.Value.Warnings.Should().BeEmpty();

        var pagesInDb = await _db.Pages.Where(p => p.ChapterId == _chapter.Id).ToListAsync();
        pagesInDb.Should().HaveCount(1);
        var panelsInDb = await _db.Panels.Where(p => p.PageId == pagesInDb[0].Id).ToListAsync();
        panelsInDb.Should().HaveCount(1);
        panelsInDb[0].Direction.Should().Be("Toàn cảnh góc rộng.");

        var linesInDb = await _db.ScriptLines.Where(l => l.PanelId == panelsInDb[0].Id).ToListAsync();
        linesInDb.Should().HaveCount(1);
        linesInDb[0].Text.Should().Be("Minh bước vào quán trọ.");
        linesInDb[0].Kind.Should().Be(ScriptLineKind.Action);

        var versionInDb = await _db.ScriptVersions.FirstOrDefaultAsync(v => v.Id == result.Value.RevisionId);
        versionInDb.Should().NotBeNull();
        versionInDb!.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task SaveChapterScript_OnGridChapter_GeneratesCellsForNewPages()
    {
        // Chương đã dùng lưới (có ô cấp chương) nhưng chưa có trang
        var pipeline = PipelineDefinition.Create(_studio.Id, "Pipeline", seriesId: _series.Id);
        var pencil = pipeline.InsertStage("Pencil", null, null, null, WorkspaceRole.Artist, false, null);
        var review = pipeline.InsertStage("Review", null, null, null, WorkspaceRole.Editor, true, null);
        _series.SetPipelineDefinition(pipeline.Id);
        _db.PipelineDefinitions.Add(pipeline);
        _db.StageTasks.Add(StageTask.Create(_series.Id, _chapter.Id, review.Id, null));
        await _db.SaveChangesAsync();

        var body = new ChapterScriptBody(null,
        [
            new ScriptTreePage(null, 1, [new ScriptTreePanel(null, 1, "Toàn cảnh.", null, [new ScriptTreeLine(null, ScriptLineKind.Action, null, "Mở đầu.")])]),
            new ScriptTreePage(null, 2, [new ScriptTreePanel(null, 1, "Cận cảnh.", null, [new ScriptTreeLine(null, ScriptLineKind.Action, null, "Tiếp.")])]),
        ]);

        var result = await CreateSaveHandler().Handle(new SaveChapterScriptCommand(_chapter.Id, _writerId, body), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await _db.StageTasks.CountAsync(t => t.PipelineStageId == pencil.Id && t.PageId != null))
            .Should().Be(2, "mỗi trang kịch bản vừa tạo có ngay ô Pencil, Producer khỏi bấm Sinh / Đồng bộ");
    }

    [Fact]
    public async Task SaveChapterScript_SameContent_NoNewVersion()
    {
        var body = new ChapterScriptBody(
            ExpectedRevisionId: null,
            Pages:
            [
                new ScriptTreePage(
                    Id: null,
                    Number: 1,
                    Panels:
                    [
                        new ScriptTreePanel(
                            Id: null,
                            Number: 1,
                            Direction: "Toàn cảnh.",
                            Lines:
                            [
                                new ScriptTreeLine(null, ScriptLineKind.Action, null, "Trời bắt đầu đổ mưa.")
                            ])
                    ])
            ]);

        var handler = CreateSaveHandler();
        var firstResult = await handler.Handle(new SaveChapterScriptCommand(_chapter.Id, _writerId, body), default);
        firstResult.IsSuccess.Should().BeTrue();
        firstResult.Value.Created.Should().BeTrue();

        var secondBody = new ChapterScriptBody(
            ExpectedRevisionId: firstResult.Value.RevisionId,
            Pages: body.Pages);

        var secondResult = await handler.Handle(new SaveChapterScriptCommand(_chapter.Id, _writerId, secondBody), default);
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value.Created.Should().BeFalse();
        secondResult.Value.RevisionId.Should().Be(firstResult.Value.RevisionId);
        secondResult.Value.VersionNumber.Should().Be(firstResult.Value.VersionNumber);

        var allVersions = await _db.ScriptVersions.Where(v => v.ChapterId == _chapter.Id).ToListAsync();
        allVersions.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveChapterScript_StaleExpectedRevisionId_Returns409Conflict()
    {
        var body = new ChapterScriptBody(
            ExpectedRevisionId: Guid.NewGuid(),
            Pages:
            [
                new ScriptTreePage(null, 1, [new ScriptTreePanel(null, 1, "Góc cận.", Lines: [new ScriptTreeLine(null, ScriptLineKind.Action, null, "Hành động.")])])
            ]);

        var handler = CreateSaveHandler();
        var result = await handler.Handle(new SaveChapterScriptCommand(_chapter.Id, _writerId, body), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("phiên bản khác");
    }

    [Fact]
    public async Task SaveChapterScript_ProducerUser_Returns403Forbidden()
    {
        var body = new ChapterScriptBody(
            ExpectedRevisionId: null,
            Pages:
            [
                new ScriptTreePage(null, 1, [new ScriptTreePanel(null, 1, "Góc xa.", Lines: [new ScriptTreeLine(null, ScriptLineKind.Action, null, "Hành động.")])])
            ]);

        var handler = CreateSaveHandler();
        var result = await handler.Handle(new SaveChapterScriptCommand(_chapter.Id, _producerId, body), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task SaveChapterScript_RefsOnDialogue_Returns400BadRequest_AndRollsBack()
    {
        var body = new ChapterScriptBody(
            ExpectedRevisionId: null,
            Pages:
            [
                new ScriptTreePage(
                    null, 1,
                    [
                        new ScriptTreePanel(
                            null, 1, "Trung cảnh.", null,
                            [
                                new ScriptTreeLine(
                                    null,
                                    ScriptLineKind.Dialogue,
                                    "Minh",
                                    "Chào bạn!",
                                    [new ScriptLineRefInput("Minh")])
                            ])
                    ])
            ]);

        var handler = CreateSaveHandler();
        var result = await handler.Handle(new SaveChapterScriptCommand(_chapter.Id, _writerId, body), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        result.ErrorMessage.Should().Contain("Chỉ cho phép gắn tham chiếu Series Bible trên dòng Action, Narration hoặc Sfx");

        var pagesInDb = await _db.Pages.Where(p => p.ChapterId == _chapter.Id).ToListAsync();
        pagesInDb.Should().BeEmpty();
        var linesInDb = await _db.ScriptLines.Where(l => l.Scene.ChapterId == _chapter.Id).ToListAsync();
        linesInDb.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveChapterScript_PanelWithArtwork_KeptWithWarning()
    {
        var page = Page.CreateNew(_chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        var panel1 = Panel.Create(page.Id, 1, 1, "0,0,600,900");
        var panel2 = Panel.Create(page.Id, 2, 2, "600,0,600,900");
        _db.Pages.Add(page);
        _db.Panels.AddRange(panel1, panel2);

        var artwork = AssetReference.Create(
            Guid.NewGuid(), _studio.Id, _chapter.Id, page.Id, Guid.NewGuid(), 1, "artwork", "panel2_lineart.png", _writerId, panel2.Id);
        _db.AssetReferences.Add(artwork);
        await _db.SaveChangesAsync();

        var body = new ChapterScriptBody(
            ExpectedRevisionId: null,
            Pages:
            [
                new ScriptTreePage(
                    page.Id, 1,
                    [
                        new ScriptTreePanel(panel1.Id, 1, "Cận cảnh.", null, [new ScriptTreeLine(null, ScriptLineKind.Action, null, "Minh nhìn quanh.")])
                    ])
            ]);

        var handler = CreateSaveHandler();
        var result = await handler.Handle(new SaveChapterScriptCommand(_chapter.Id, _writerId, body), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Warnings.Should().Contain(w => w.Contains("Panel 2") && w.Contains("artwork") && w.Contains("giữ lại"));

        var panelsInDb = await _db.Panels.Where(p => p.PageId == page.Id).ToListAsync();
        panelsInDb.Should().Contain(p => p.Id == panel2.Id);
    }

    [Fact]
    public async Task SubmitChapterScript_WithBlocker_SucceedsWithWarning_AllowsSubmitBeforeBibleCompleted()
    {
        var mockGate = new Mock<IChapterCastGate>();
        mockGate.Setup(g => g.EvaluateSubmitAsync(_chapter.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChapterCastSubmitGateDto(
                false,
                [new ChapterCastBlockerDto(Guid.NewGuid(), "Kiếm Thần", null, "Chưa duyệt thiết kế.")]));

        var trackingUow = new TrackingTestUnitOfWork();
        var handler = CreateSubmitHandler(gate: mockGate.Object, uow: trackingUow);

        var body = new ChapterScriptBody(
            ExpectedRevisionId: null,
            Pages:
            [
                new ScriptTreePage(null, 1, [new ScriptTreePanel(null, 1, "Toàn cảnh.", null, [new ScriptTreeLine(null, ScriptLineKind.Action, null, "Xuất hiện.")])])
            ]);

        var result = await handler.Handle(new SubmitChapterScriptCommand(_chapter.Id, _writerId, body), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Warnings.Should().Contain(w => w.Contains("Kiếm Thần: Chưa duyệt thiết kế."));
        result.Value.Chapter.ScriptStatus.Should().Be(ScriptStatus.InReview);

        var refreshedChapter = await _db.Chapters.FirstAsync(c => c.Id == _chapter.Id);
        refreshedChapter.ScriptStatus.Should().Be(ScriptStatus.InReview);
        refreshedChapter.LastSubmittedVersionId.Should().Be(result.Value.RevisionId);
    }

    [Fact]
    public async Task SubmitChapterScript_Ok_ChangesStatus()
    {
        var body = new ChapterScriptBody(
            ExpectedRevisionId: null,
            Pages:
            [
                new ScriptTreePage(null, 1, [new ScriptTreePanel(null, 1, "Toàn cảnh.", null, [new ScriptTreeLine(null, ScriptLineKind.Action, null, "Một ngày mới bắt đầu.")])])
            ]);

        var handler = CreateSubmitHandler();
        var result = await handler.Handle(new SubmitChapterScriptCommand(_chapter.Id, _writerId, body), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Chapter.ScriptStatus.Should().Be(ScriptStatus.InReview);
        result.Value.Chapter.LastSubmittedVersionId.Should().Be(result.Value.RevisionId);

        var refreshedChapter = await _db.Chapters.FirstAsync(c => c.Id == _chapter.Id);
        refreshedChapter.ScriptStatus.Should().Be(ScriptStatus.InReview);
        refreshedChapter.LastSubmittedVersionId.Should().Be(result.Value.RevisionId);
        refreshedChapter.ScriptSubmittedAt.Should().NotBeNull();
    }

    private sealed class TrackingTestUnitOfWork : IUnitOfWork
    {
        public bool RolledBack { get; private set; }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch
            {
                RolledBack = true;
                throw;
            }
        }
    }
}
