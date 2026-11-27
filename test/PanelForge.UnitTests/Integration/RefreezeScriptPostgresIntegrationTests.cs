using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assignments.Commands.StartChapterProduction;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Application.Features.Chapters.Commands.RefreezeScript;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;
using PanelForge.Application.Features.Chapters.Queries.GetRefreezeScriptPreview;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptLines.Commands.SaveChapterScript;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Integration;

public sealed class RefreezeScriptPostgresIntegrationTests
{
    private static readonly string AdminConn = PostgresTestConnection.Admin;

    private static async Task ExecuteAdminSqlAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(AdminConn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class PgUnitOfWork(PanelForgeDbContext db) : IUnitOfWork
    {
        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await operation(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }

    private sealed class InMemoryPageRepository : IPageRepository
    {
        private readonly Dictionary<Guid, Page> _store = new();

        public void Seed(Page page) => _store[page.Id] = page;

        public Task<Page?> GetAsync(Guid pageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.GetValueOrDefault(pageId));

        public Task<Page?> GetAtVersionAsync(Guid pageId, long version, CancellationToken ct = default) =>
            Task.FromResult(_store.GetValueOrDefault(pageId));

        public Task<long> GetStreamVersionAsync(Guid pageId, CancellationToken ct = default) =>
            Task.FromResult(_store.ContainsKey(pageId) ? 1L : -1L);

        public Task<IReadOnlyDictionary<Guid, long>> GetStreamVersionsAsync(IReadOnlyCollection<Guid> pageIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, long>>(pageIds.ToDictionary(id => id, id => _store.ContainsKey(id) ? 1L : -1L));

        public Task<IReadOnlyList<PanelForge.Domain.Common.IDomainEvent>> GetEventsAsync(Guid pageId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PanelForge.Domain.Common.IDomainEvent>>([]);

        public Task SaveAsync(Page page, CancellationToken ct = default)
        {
            _store[page.Id] = page;
            return Task.CompletedTask;
        }
    }

    private static async Task RunWithTempPostgresAsync(Func<PanelForgeDbContext, Task> testBody)
    {
        var tempDbName = $"panelforge_refreeze_{Guid.NewGuid():N}";
        var tempConn = PostgresTestConnection.ForDatabase(tempDbName);
        await ExecuteAdminSqlAsync($"CREATE DATABASE \"{tempDbName}\";");

        try
        {
            var options = new DbContextOptionsBuilder<PanelForgeDbContext>()
                .UseNpgsql(tempConn)
                .Options;

            await using var db = new PanelForgeDbContext(options);
            await db.Database.MigrateAsync();
            db.Database.HasPendingModelChanges().Should().BeFalse();

            await testBody(db);
        }
        finally
        {
            await ExecuteAdminSqlAsync(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{tempDbName}' AND pid <> pg_backend_pid();");
            await ExecuteAdminSqlAsync($"DROP DATABASE IF EXISTS \"{tempDbName}\";");
        }
    }

    private sealed record SeedContext(
        StudioWorkspace Studio,
        DomainSeries Series,
        Chapter Chapter,
        Scene Scene,
        User Producer,
        User Writer,
        User Artist,
        User Letterer,
        PipelineStage PencilStage,
        PipelineStage LetterStage,
        List<Page> Pages,
        List<Panel> Panels,
        List<ScriptLine> Lines,
        InMemoryPageRepository PageRepo,
        IWorkspaceAuthorizationService Auth,
        IChapterCastGate CastGate,
        IChapterCastService CastService,
        IUnitOfWork Uow);

    private static async Task<SeedContext> SeedStartedProductionChapterAsync(
        PanelForgeDbContext db,
        int initialPageCount = 3)
    {
        var producer = User.Create($"producer.{Guid.NewGuid():N}@pf.local", "Producer");
        var writer = User.Create($"writer.{Guid.NewGuid():N}@pf.local", "Writer");
        var artist = User.Create($"artist.{Guid.NewGuid():N}@pf.local", "Artist");
        var letterer = User.Create($"letterer.{Guid.NewGuid():N}@pf.local", "Letterer");

        var studio = StudioWorkspace.Create("Refreeze Studio", producer.Id);
        var series = DomainSeries.Create(studio.Id, "Refreeze Manga", ReadingDirection.RightToLeft);
        var pipeline = PipelineDefinition.Create(studio.Id, "Standard", seriesId: series.Id);
        var pencil = pipeline.AddStage("Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
        var letter = pipeline.AddStage("Lettering", "lettering", 2, allowedRole: WorkspaceRole.Letterer);
        series.SetPipelineDefinition(pipeline.Id);

        var chapter = Chapter.Create(series.Id, 1m, "Chương Test");
        var scene = Scene.CreateDefault(chapter.Id);

        db.Users.AddRange(producer, writer, artist, letterer);
        db.StudioWorkspaces.Add(studio);
        db.Series.Add(series);
        db.PipelineDefinitions.Add(pipeline);
        db.Chapters.Add(chapter);
        db.Scenes.Add(scene);
        db.WorkspaceMembers.AddRange(
            WorkspaceMember.Create(studio.Id, producer.Id, WorkspaceRole.Producer),
            WorkspaceMember.Create(studio.Id, writer.Id, WorkspaceRole.Writer),
            WorkspaceMember.Create(studio.Id, artist.Id, WorkspaceRole.Artist),
            WorkspaceMember.Create(studio.Id, letterer.Id, WorkspaceRole.Letterer));

        var pageRepo = new InMemoryPageRepository();
        var pages = new List<Page>();
        var panels = new List<Panel>();
        var lines = new List<ScriptLine>();

        for (var i = 1; i <= initialPageCount; i++)
        {
            var page = Page.CreateNew(chapter.Id, i, LayoutFormat.StandardPage, 1200, 1800, 300);
            pageRepo.Seed(page);
            pages.Add(page);

            var panel = Panel.Create(page.Id, 1, 1, "{}", direction: $"Khung trang {i}");
            panels.Add(panel);

            var action = ScriptLine.Create(scene.Id, (i - 1) * 2 + 1, stageDirection: $"Hành động trang {i}", kind: ScriptLineKind.Action);
            action.AssignToPanel(panel.Id);
            var dialogue = ScriptLine.Create(scene.Id, (i - 1) * 2 + 2, dialogueText: $"Thoại trang {i}", kind: ScriptLineKind.Dialogue, speakerLabel: "Akira");
            dialogue.AssignToPanel(panel.Id);
            lines.Add(action);
            lines.Add(dialogue);
        }

        db.Pages.AddRange(pages);
        db.Panels.AddRange(panels);
        db.ScriptLines.AddRange(lines);
        await db.SaveChangesAsync();

        var resolver = new ChapterCastInheritanceResolver(db);
        var castService = new ChapterCastService(db, resolver);
        var castGateMock = new Mock<IChapterCastGate>();
        castGateMock.Setup(g => g.EvaluateStartAsync(chapter.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChapterCastStartGateDto(true, []));
        castGateMock.Setup(g => g.EvaluateSubmitAsync(chapter.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChapterCastSubmitGateDto(true, []));

        var authMock = new Mock<IWorkspaceAuthorizationService>();
        authMock.Setup(a => a.GetMemberRoleAsync(producer.Id, studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Producer);
        authMock.Setup(a => a.GetSeriesMemberRoleAsync(producer.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Producer);
        authMock.Setup(a => a.GetMemberRoleAsync(writer.Id, studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
        authMock.Setup(a => a.GetSeriesMemberRoleAsync(writer.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
        authMock.Setup(a => a.GetMemberRoleAsync(artist.Id, studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Artist);
        authMock.Setup(a => a.GetSeriesMemberRoleAsync(artist.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Artist);

        await castService.RecomputeAsync(chapter.Id, writer.Id, default);
        await db.SaveChangesAsync();

        var snapshot = await ScriptSnapshotBuilder.BuildWithChoicesAsync(db, chapter.Id, default);
        var v1 = ScriptVersion.Create(
            chapter.Id, 1, "Initial submit", writer.Id, snapshot.Hash,
            ScriptSnapshotBuilder.Serialize(snapshot.Rows, snapshot.CastChoices), snapshot.LineCount);
        v1.MarkSubmitted(writer.Id);
        db.ScriptVersions.Add(v1);
        chapter.SubmitScript(writer.Id, v1.Id);
        await db.SaveChangesAsync();

        var uow = new PgUnitOfWork(db);
        var startHandler = new StartChapterProductionCommandHandler(db, uow, castGateMock.Object);
        var startResult = await startHandler.Handle(
            new StartChapterProductionCommand(
                chapter.Id,
                producer.Id,
                [new ProductionPageAssignmentRequest(artist.Id, 1, initialPageCount)],
                [new ProductionPageAssignmentRequest(letterer.Id, 1, initialPageCount)]),
            default);
        startResult.IsSuccess.Should().BeTrue(startResult.ErrorMessage);

        return new SeedContext(
            studio, series, chapter, scene, producer, writer, artist, letterer,
            pencil, letter, pages, panels, lines, pageRepo,
            authMock.Object, castGateMock.Object, castService, uow);
    }

    private static ChapterScriptBody BuildBodyFromSeed(
        SeedContext ctx,
        IEnumerable<int> includePages,
        Action<List<ScriptTreePage>>? mutate = null)
    {
        var treePages = new List<ScriptTreePage>();
        foreach (var pageNum in includePages)
        {
            var page = ctx.Pages.Single(p => p.PageNumber == pageNum);
            var panel = ctx.Panels.Single(pn => pn.PageId == page.Id);
            var panelLines = ctx.Lines
                .Where(l => l.PanelId == panel.Id)
                .OrderBy(l => l.LineOrder)
                .Select(l => new ScriptTreeLine(l.Id, l.Kind, l.SpeakerLabel, l.Text ?? string.Empty))
                .ToList();

            treePages.Add(new ScriptTreePage(
                page.Id,
                page.PageNumber,
                [new ScriptTreePanel(panel.Id, panel.PanelNumber, panel.Direction, null, panelLines)]));
        }

        mutate?.Invoke(treePages);
        return new ChapterScriptBody(ctx.Chapter.LastSubmittedVersionId, treePages);
    }

    [Fact]
    public async Task RefreezeScript_WithAddedPages_RequiresAssignments_GeneratesTasksOnlyForNewPages()
    {
        await RunWithTempPostgresAsync(async db =>
        {
            var ctx = await SeedStartedProductionChapterAsync(db, initialPageCount: 3);
            var oldTaskIds = await db.StageTasks.AsNoTracking().Select(t => t.Id).ToListAsync();

            ctx.Chapter.UnfreezeScript(ctx.Producer.Id, "Bổ sung trang 4");
            await db.SaveChangesAsync();

            var body = BuildBodyFromSeed(ctx, [1, 2, 3], pages =>
            {
                pages.Add(new ScriptTreePage(null, 4,
                [
                    new ScriptTreePanel(null, 1, "Khung trang 4", null,
                    [
                        new ScriptTreeLine(null, ScriptLineKind.Action, null, "Nhân vật xuất hiện ở trang 4.")
                    ])
                ]));
            });

            var submitHandler = new SubmitChapterScriptCommandHandler(
                db, ctx.Auth, ctx.PageRepo, ctx.CastService, ctx.CastGate, ctx.Uow);
            var submitResult = await submitHandler.Handle(
                new SubmitChapterScriptCommand(ctx.Chapter.Id, ctx.Writer.Id, body, "Thêm trang 4"), default);
            submitResult.IsSuccess.Should().BeTrue(submitResult.ErrorMessage);

            var previewHandler = new GetRefreezeScriptPreviewQueryHandler(db, ctx.PageRepo, ctx.CastGate, ctx.Auth);
            var preview = await previewHandler.Handle(new GetRefreezeScriptPreviewQuery(ctx.Chapter.Id, ctx.Producer.Id), default);
            preview.IsSuccess.Should().BeTrue(preview.ErrorMessage);
            preview.Value!.AddedPages.Should().ContainSingle(p => p.PageNumber == 4);

            var refreezeHandler = new RefreezeScriptCommandHandler(db, ctx.Uow, ctx.PageRepo, ctx.CastGate, ctx.Auth);

            // Missing assignment must fail
            var missingRes = await refreezeHandler.Handle(
                new RefreezeScriptCommand(ctx.Chapter.Id, ctx.Producer.Id, preview.Value.HashParity.SubmittedHash!, []),
                default);
            missingRes.IsSuccess.Should().BeFalse();

            // Valid 100% assignment succeeds
            var okRes = await refreezeHandler.Handle(
                new RefreezeScriptCommand(
                    ctx.Chapter.Id,
                    ctx.Producer.Id,
                    preview.Value.HashParity.SubmittedHash!,
                    [new NewPageAssignmentRequest(4, ctx.Artist.Id, ctx.Letterer.Id)]),
                default);
            okRes.IsSuccess.Should().BeTrue(okRes.ErrorMessage);
            okRes.Value!.AddedPagesCount.Should().Be(1);

            var refreshedChapter = await db.Chapters.AsNoTracking().SingleAsync(c => c.Id == ctx.Chapter.Id);
            refreshedChapter.IsScriptFrozen.Should().BeTrue();
            refreshedChapter.PreviousFrozenScriptVersionId.Should().NotBeNull();

            var allTasks = await db.StageTasks.AsNoTracking().ToListAsync();
            allTasks.Should().HaveCount(8); // 4 pages * 2 stages
            allTasks.Where(t => oldTaskIds.Contains(t.Id)).Should().HaveCount(6);
            var page4 = await db.Pages.AsNoTracking().SingleAsync(p => p.ChapterId == ctx.Chapter.Id && p.PageNumber == 4);
            allTasks.Where(t => t.PageId == page4.Id).Should().HaveCount(2)
                .And.AllSatisfy(t => t.Status.Should().Be(StageTaskStatus.Assigned));
        });
    }

    [Fact]
    public async Task RefreezeScript_WithRemovedPages_CancelsTasks_WhenNoArtwork()
    {
        await RunWithTempPostgresAsync(async db =>
        {
            var ctx = await SeedStartedProductionChapterAsync(db, initialPageCount: 3);
            var page3 = ctx.Pages.Single(p => p.PageNumber == 3);
            var tasksBeforeSubmit = await db.StageTasks.AsNoTracking().Where(t => t.PageId == page3.Id).ToListAsync();
            tasksBeforeSubmit.Should().HaveCount(2, "page3 phải có 2 StageTask trước khi Writer xoá trang");

            ctx.Chapter.UnfreezeScript(ctx.Producer.Id, "Gỡ trang 3 trống");
            await db.SaveChangesAsync();

            var body = BuildBodyFromSeed(ctx, [1, 2]);
            var submitHandler = new SubmitChapterScriptCommandHandler(
                db, ctx.Auth, ctx.PageRepo, ctx.CastService, ctx.CastGate, ctx.Uow);
            var submitResult = await submitHandler.Handle(
                new SubmitChapterScriptCommand(ctx.Chapter.Id, ctx.Writer.Id, body, "Bỏ trang 3"), default);
            submitResult.IsSuccess.Should().BeTrue(submitResult.ErrorMessage);

            var tasksAfterSubmit = await db.StageTasks.AsNoTracking().Where(t => t.PageId == page3.Id).ToListAsync();
            tasksAfterSubmit.Should().HaveCount(2, "page3 StageTasks chưa bị xoá/huỷ trước khi Refreeze");
            tasksAfterSubmit.Should().AllSatisfy(t => t.Status.Should().NotBe(StageTaskStatus.Cancelled));

            var previewHandler = new GetRefreezeScriptPreviewQueryHandler(db, ctx.PageRepo, ctx.CastGate, ctx.Auth);
            var preview = await previewHandler.Handle(new GetRefreezeScriptPreviewQuery(ctx.Chapter.Id, ctx.Producer.Id), default);
            preview.IsSuccess.Should().BeTrue();
            preview.Value!.RemovedPages.Should().ContainSingle(p => p.PageNumber == 3 && p.CanSafelyDelete);

            var refreezeHandler = new RefreezeScriptCommandHandler(db, ctx.Uow, ctx.PageRepo, ctx.CastGate, ctx.Auth);
            var okRes = await refreezeHandler.Handle(
                new RefreezeScriptCommand(ctx.Chapter.Id, ctx.Producer.Id, preview.Value.HashParity.SubmittedHash!, []),
                default);
            okRes.IsSuccess.Should().BeTrue(okRes.ErrorMessage);
            okRes.Value!.CancelledTasksCount.Should().Be(2);

            var page3Tasks = await db.StageTasks.AsNoTracking().Where(t => t.PageId == page3.Id).ToListAsync();
            page3Tasks.Should().HaveCount(2).And.AllSatisfy(t => t.Status.Should().Be(StageTaskStatus.Cancelled));
        });
    }

    [Fact]
    public async Task RefreezeScript_WithRemovedPages_AbortsWithConflict_WhenArtworkUploadedMeanwhile()
    {
        await RunWithTempPostgresAsync(async db =>
        {
            var ctx = await SeedStartedProductionChapterAsync(db, initialPageCount: 3);
            var page3 = ctx.Pages.Single(p => p.PageNumber == 3);

            ctx.Chapter.UnfreezeScript(ctx.Producer.Id, "Gỡ trang 3");
            await db.SaveChangesAsync();

            var body = BuildBodyFromSeed(ctx, [1, 2]);
            var submitHandler = new SubmitChapterScriptCommandHandler(
                db, ctx.Auth, ctx.PageRepo, ctx.CastService, ctx.CastGate, ctx.Uow);
            var submitResult = await submitHandler.Handle(
                new SubmitChapterScriptCommand(ctx.Chapter.Id, ctx.Writer.Id, body), default);
            submitResult.IsSuccess.Should().BeTrue();

            var previewHandler = new GetRefreezeScriptPreviewQueryHandler(db, ctx.PageRepo, ctx.CastGate, ctx.Auth);
            var preview = await previewHandler.Handle(new GetRefreezeScriptPreviewQuery(ctx.Chapter.Id, ctx.Producer.Id), default);
            preview.IsSuccess.Should().BeTrue();

            // Race condition: Artist uploads artwork onto page 3 right before Producer clicks Refreeze
            var asset = Asset.Create(ctx.Studio.Id, new string('a', 64), 1024, "image/png");
            db.Assets.Add(asset);
            db.AssetReferences.Add(AssetReference.Create(
                asset.Id, ctx.Studio.Id, ctx.Chapter.Id, page3.Id, Guid.NewGuid(), 1, "artwork", "page3.png", ctx.Artist.Id));
            await db.SaveChangesAsync();

            var refreezeHandler = new RefreezeScriptCommandHandler(db, ctx.Uow, ctx.PageRepo, ctx.CastGate, ctx.Auth);
            var res = await refreezeHandler.Handle(
                new RefreezeScriptCommand(ctx.Chapter.Id, ctx.Producer.Id, preview.Value!.HashParity.SubmittedHash!, []),
                default);

            res.IsSuccess.Should().BeFalse();
            res.ErrorCode.Should().Be(ResultErrorCodes.ActiveArtworkOnRemovedPage);
            (await db.StageTasks.AsNoTracking().Where(t => t.PageId == page3.Id).ToListAsync())
                .Should().AllSatisfy(t => t.Status.Should().NotBe(StageTaskStatus.Cancelled));
        });
    }

    [Fact]
    public async Task RefreezeScript_TextOnlyEdit_DoesNotFlagArtistStageTasks_MarksBalloonsStale()
    {
        await RunWithTempPostgresAsync(async db =>
        {
            var ctx = await SeedStartedProductionChapterAsync(db, initialPageCount: 2);
            var page2 = ctx.Pages.Single(p => p.PageNumber == 2);
            var page2Panel = ctx.Panels.Single(pn => pn.PageId == page2.Id);
            var page2Dialogue = ctx.Lines.Single(l => l.PanelId == page2Panel.Id && l.Kind == ScriptLineKind.Dialogue);

            // Artist started Pencil on page 2, Letterer placed a balloon bound to page2Dialogue
            var pencilTask = await db.StageTasks.SingleAsync(t => t.PageId == page2.Id && t.PipelineStageId == ctx.PencilStage.Id);
            pencilTask.Start(prerequisitesApproved: true);
            page2.AddElement("SPEECH_BALLOON", 10, 10, 100, 60, 1, ctx.Letterer.Id, content: page2Dialogue.Text,
                scriptLineId: page2Dialogue.Id, sourceLineText: page2Dialogue.Text);
            await db.SaveChangesAsync();

            ctx.Chapter.UnfreezeScript(ctx.Producer.Id, "Sửa lời thoại trang 2");
            await db.SaveChangesAsync();

            var body = BuildBodyFromSeed(ctx, [1, 2], pages =>
            {
                var idx = pages.FindIndex(p => p.Number == 2);
                var p2 = pages[idx];
                var updatedLines = p2.Panels![0].Lines!.Select(l =>
                    l.Id == page2Dialogue.Id ? l with { Text = "Lời thoại mới đã chỉnh sửa!" } : l).ToList();
                var updatedPanel = p2.Panels[0] with { Lines = updatedLines };
                pages[idx] = p2 with { Panels = [updatedPanel] };
            });

            var submitHandler = new SubmitChapterScriptCommandHandler(
                db, ctx.Auth, ctx.PageRepo, ctx.CastService, ctx.CastGate, ctx.Uow);
            (await submitHandler.Handle(new SubmitChapterScriptCommand(ctx.Chapter.Id, ctx.Writer.Id, body), default)).IsSuccess.Should().BeTrue();

            var preview = await new GetRefreezeScriptPreviewQueryHandler(db, ctx.PageRepo, ctx.CastGate, ctx.Auth)
                .Handle(new GetRefreezeScriptPreviewQuery(ctx.Chapter.Id, ctx.Producer.Id), default);
            preview.IsSuccess.Should().BeTrue();
            preview.Value!.VisualImpacts.Should().BeEmpty();
            preview.Value.TextImpacts.Should().ContainSingle(ti => ti.LineId == page2Dialogue.Id && ti.IsStale);

            var refreezeRes = await new RefreezeScriptCommandHandler(db, ctx.Uow, ctx.PageRepo, ctx.CastGate, ctx.Auth)
                .Handle(new RefreezeScriptCommand(ctx.Chapter.Id, ctx.Producer.Id, preview.Value.HashParity.SubmittedHash!, []), default);
            refreezeRes.IsSuccess.Should().BeTrue(refreezeRes.ErrorMessage);
            refreezeRes.Value!.VisualImpactCount.Should().Be(0);
            refreezeRes.Value.TextImpactCount.Should().Be(1);

            var refreshedPencil = await db.StageTasks.AsNoTracking().SingleAsync(t => t.Id == pencilTask.Id);
            refreshedPencil.IsInputOutdated.Should().BeFalse("chỉ sửa lời thoại không được gắn cờ Họa sĩ");

            var analysis = await LetteringPageAnalysis.AnalyzeAsync(db, page2, default);
            analysis.StaleElementIds.Should().ContainSingle();
        });
    }

    [Fact]
    public async Task RefreezeScript_VisualEdit_FlagsArtistStageTasks_WhenStarted()
    {
        await RunWithTempPostgresAsync(async db =>
        {
            var ctx = await SeedStartedProductionChapterAsync(db, initialPageCount: 2);
            var page1 = ctx.Pages.Single(p => p.PageNumber == 1);
            var page1Panel = ctx.Panels.Single(pn => pn.PageId == page1.Id);
            var page1Action = ctx.Lines.Single(l => l.PanelId == page1Panel.Id && l.Kind == ScriptLineKind.Action);

            var pencilTask = await db.StageTasks.SingleAsync(t => t.PageId == page1.Id && t.PipelineStageId == ctx.PencilStage.Id);
            pencilTask.Start(prerequisitesApproved: true);
            await db.SaveChangesAsync();

            ctx.Chapter.UnfreezeScript(ctx.Producer.Id, "Sửa hành động khung 1 trang 1");
            await db.SaveChangesAsync();

            var body = BuildBodyFromSeed(ctx, [1, 2], pages =>
            {
                var idx = pages.FindIndex(p => p.Number == 1);
                var p1 = pages[idx];
                var updatedLines = p1.Panels![0].Lines!.Select(l =>
                    l.Id == page1Action.Id ? l with { Text = "Nhân vật nhảy qua cửa sổ!" } : l).ToList();
                var updatedPanel = p1.Panels[0] with { Lines = updatedLines };
                pages[idx] = p1 with { Panels = [updatedPanel] };
            });

            var submitHandler = new SubmitChapterScriptCommandHandler(
                db, ctx.Auth, ctx.PageRepo, ctx.CastService, ctx.CastGate, ctx.Uow);
            (await submitHandler.Handle(new SubmitChapterScriptCommand(ctx.Chapter.Id, ctx.Writer.Id, body), default)).IsSuccess.Should().BeTrue();

            var preview = await new GetRefreezeScriptPreviewQueryHandler(db, ctx.PageRepo, ctx.CastGate, ctx.Auth)
                .Handle(new GetRefreezeScriptPreviewQuery(ctx.Chapter.Id, ctx.Producer.Id), default);
            preview.IsSuccess.Should().BeTrue();
            preview.Value!.VisualImpacts.Should().ContainSingle(vi => vi.PageNumber == 1 && vi.ChangeType == "ActionEdited");

            var refreezeRes = await new RefreezeScriptCommandHandler(db, ctx.Uow, ctx.PageRepo, ctx.CastGate, ctx.Auth)
                .Handle(new RefreezeScriptCommand(ctx.Chapter.Id, ctx.Producer.Id, preview.Value.HashParity.SubmittedHash!, []), default);
            refreezeRes.IsSuccess.Should().BeTrue(refreezeRes.ErrorMessage);
            refreezeRes.Value!.VisualImpactCount.Should().Be(1);

            var refreshedPencil = await db.StageTasks.AsNoTracking().SingleAsync(t => t.Id == pencilTask.Id);
            refreshedPencil.IsInputOutdated.Should().BeTrue();
            refreshedPencil.OutdatedReason.Should().Be(StageTask.ScriptChangedReason);
        });
    }

    [Fact]
    public async Task SaveChapterScript_InProduction_RejectsDeletingPageWithArtworkOrActiveTasks()
    {
        await RunWithTempPostgresAsync(async db =>
        {
            var ctx = await SeedStartedProductionChapterAsync(db, initialPageCount: 2);
            var page2 = ctx.Pages.Single(p => p.PageNumber == 2);

            var pencilTask = await db.StageTasks.SingleAsync(t => t.PageId == page2.Id && t.PipelineStageId == ctx.PencilStage.Id);
            pencilTask.Start(prerequisitesApproved: true);
            await db.SaveChangesAsync();

            ctx.Chapter.UnfreezeScript(ctx.Producer.Id, "Mở sửa");
            await db.SaveChangesAsync();

            // Writer drops Page 2 while Page 2 has started work
            var body = BuildBodyFromSeed(ctx, [1]);
            var saveHandler = new SaveChapterScriptCommandHandler(db, ctx.Auth, ctx.PageRepo, ctx.CastService, ctx.Uow);
            var res = await saveHandler.Handle(new SaveChapterScriptCommand(ctx.Chapter.Id, ctx.Writer.Id, body), default);

            res.IsSuccess.Should().BeFalse();
            res.ErrorCode.Should().Be(ResultErrorCodes.CannotDeleteActiveProductionPage);
            res.ErrorDetails.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task SaveChapterScript_EnforcesThoughtSpeakerInvariant()
    {
        await RunWithTempPostgresAsync(async db =>
        {
            var ctx = await SeedStartedProductionChapterAsync(db, initialPageCount: 1);
            ctx.Chapter.UnfreezeScript(ctx.Producer.Id, "Thử dòng thought thiếu speaker");
            await db.SaveChangesAsync();

            var body = BuildBodyFromSeed(ctx, [1], pages =>
            {
                var linesList = pages[0].Panels![0].Lines!.ToList();
                linesList.Add(new ScriptTreeLine(null, ScriptLineKind.Thought, null, "Suy nghĩ không có người nói"));
                var updatedPanel = pages[0].Panels[0] with { Lines = linesList };
                pages[0] = pages[0] with { Panels = [updatedPanel] };
            });

            var saveHandler = new SaveChapterScriptCommandHandler(db, ctx.Auth, ctx.PageRepo, ctx.CastService, ctx.Uow);
            var res = await saveHandler.Handle(new SaveChapterScriptCommand(ctx.Chapter.Id, ctx.Writer.Id, body), default);

            res.IsSuccess.Should().BeFalse();
            res.ErrorCode.Should().Be(ResultErrorCodes.Validation);
            res.ErrorMessage.Should().Contain("Thought");
        });
    }
}
