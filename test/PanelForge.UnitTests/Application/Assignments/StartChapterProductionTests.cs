using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Assignments.Commands.StartChapterProduction;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Assignments;

public sealed class StartChapterProductionTests
{
    private sealed class Fixture
    {
        public readonly PanelForgeDbContext Db = InMemoryDb.Create();
        public readonly PassThroughUnitOfWork UnitOfWork = new();
        public readonly Mock<IChapterCastGate> Gate = new();
        public readonly Guid ProducerId = Guid.NewGuid();
        public readonly Guid WriterId = Guid.NewGuid();
        public readonly Guid Artist1Id = Guid.NewGuid();
        public readonly Guid Artist2Id = Guid.NewGuid();
        public readonly Chapter Chapter;
        public readonly Scene Scene;

        public Fixture()
        {
            var studio = StudioWorkspace.Create("Studio", ProducerId);
            var series = DomainSeries.Create(studio.Id, "Series", ReadingDirection.LeftToRight);
            Chapter = Chapter.Create(series.Id, 1, "Chapter 1");
            Scene = Scene.Create(Chapter.Id, 1, "Scene");
            var pipeline = PipelineDefinition.Create(studio.Id, "Production", seriesId: series.Id);
            var pencil = pipeline.AddStage(
                "Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
            var letter = pipeline.AddStage(
                "Lettering", "lettering", 2, allowedRole: WorkspaceRole.Letterer);

            Db.AddRange(studio, series, Chapter, Scene, pipeline, pencil, letter);
            Db.WorkspaceMembers.AddRange(
                WorkspaceMember.Create(studio.Id, WriterId, WorkspaceRole.Writer),
                WorkspaceMember.Create(studio.Id, Artist1Id, WorkspaceRole.Artist),
                WorkspaceMember.Create(studio.Id, Artist2Id, WorkspaceRole.Artist));
            for (var pageNumber = 1; pageNumber <= 4; pageNumber++)
                Db.Pages.Add(Page.CreateNew(Chapter.Id, pageNumber, LayoutFormat.StandardPage, 1200, 1800, 300));
            Db.ScriptLines.Add(ScriptLine.Create(Scene.Id, 1, stageDirection: "Opening action"));
            Db.SaveChanges();

            var snapshot = ScriptSnapshotBuilder.BuildAsync(Db, Chapter.Id, default).GetAwaiter().GetResult();
            var rowsJson = ScriptSnapshotBuilder.Serialize(snapshot.Rows);
            var version = ScriptVersion.Create(
                Chapter.Id, 1, "Submitted", WriterId, snapshot.Hash, rowsJson, snapshot.LineCount);
            version.MarkSubmitted(WriterId);
            Db.ScriptVersions.Add(version);
            Chapter.SubmitScript(WriterId, version.Id);
            Db.SaveChanges();

            Gate.Setup(g => g.EvaluateStartAsync(Chapter.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChapterCastStartGateDto(true, []));
        }

        public StartChapterProductionCommandHandler Handler => new(Db, UnitOfWork, Gate.Object);

        public StartChapterProductionCommand Command(
            IReadOnlyList<ProductionPageAssignmentRequest>? artists = null) =>
            new(Chapter.Id, ProducerId, artists ??
            [
                new(Artist1Id, 1, 2),
                new(Artist2Id, 3, 4)
            ]);
    }

    [Fact]
    public async Task HappyPath_FreezesScript_CreatesAssignments_AndStartsProduction()
    {
        var f = new Fixture();

        var result = await f.Handler.Handle(f.Command(), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Chapter.ScriptStatus.Should().Be(ScriptStatus.Frozen);
        result.Value.Chapter.Status.Should().Be(ChapterStatus.InProduction);
        result.Value.Assignments.Should().HaveCount(4);
        result.Value.Assignments.Should().OnlyContain(a => a.EntityType == WorkflowEntityType.Page);
        f.Db.AuditLogs.Should().ContainSingle(a => a.Action == "START_CHAPTER_PRODUCTION");
        f.UnitOfWork.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task ChapterOnTaskGrid_Returns409_AndCreatesNoAssignments()
    {
        var f = new Fixture();
        f.Db.StageTasks.Add(StageTask.Create(f.Chapter.SeriesId, f.Chapter.Id, Guid.NewGuid(), null));
        f.Db.SaveChanges();

        var result = await f.Handler.Handle(f.Command(), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        f.Db.Assignments.Should().BeEmpty();
        f.Chapter.Status.Should().Be(ChapterStatus.PreProduction);
    }

    [Fact]
    public async Task OverlappingRanges_Return409NamingBothRanges()
    {
        var f = new Fixture();
        var command = f.Command([
            new(f.Artist1Id, 1, 3),
            new(f.Artist2Id, 3, 4)
        ]);

        var result = await f.Handler.Handle(command, default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("1-3").And.Contain("3-4");
        f.Chapter.Status.Should().Be(ChapterStatus.PreProduction);
        f.Db.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task BibleBlockers_Return409WithStructuredBlockerList()
    {
        var f = new Fixture();
        var blocker = new ChapterCastBlockerDto(Guid.NewGuid(), "Hero", null, "Canon chưa khóa");
        f.Gate.Setup(g => g.EvaluateStartAsync(f.Chapter.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChapterCastStartGateDto(false, [blocker]));

        var result = await f.Handler.Handle(f.Command(), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorDetails.Should().BeEquivalentTo(new[] { blocker });
        f.Chapter.ScriptStatus.Should().Be(ScriptStatus.InReview);
        f.Db.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenScriptComments_DoNotBlockStartProduction()
    {
        var f = new Fixture();
        f.Db.ScriptComments.Add(ScriptComment.Create(
            f.Chapter.Id, f.Scene.Id, null, null, "Please reconsider this line", f.ProducerId));
        await f.Db.SaveChangesAsync();

        var result = await f.Handler.Handle(f.Command(), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        f.Db.ScriptComments.Single().IsResolved.Should().BeFalse();
        f.Chapter.Status.Should().Be(ChapterStatus.InProduction);
    }

    [Fact]
    public async Task AssignmentStepFailure_LeavesAllGate1ChangesRolledBack()
    {
        var f = new Fixture();
        var nonMember = Guid.NewGuid();

        var result = await f.Handler.Handle(
            f.Command([new(nonMember, 1, 4)]), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        f.Chapter.ScriptStatus.Should().Be(ScriptStatus.InReview);
        f.Chapter.Status.Should().Be(ChapterStatus.PreProduction);
        f.Db.Assignments.Should().BeEmpty();
        f.Db.AuditLogs.Should().BeEmpty();
        f.UnitOfWork.ExecutionCount.Should().Be(1);
    }
}
