using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.CanvasElements.Commands.AddElement;
using PanelForge.Application.Features.Progress.Queries.GetSeriesProgress;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Progress;

/// <summary>CF2: cổng nộp Lettering chặn khi thoại của panel chưa có balloon; UC-16: tổng hợp tiến độ Series cho Producer.</summary>
public class LetteringGateAndSeriesProgressTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IPageRepository> _repo = new();
    private readonly StudioWorkspace _studio = StudioWorkspace.Create("Studio", Guid.NewGuid());
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Page _page;
    private readonly Panel _panel;
    private readonly Guid _letterer = Guid.NewGuid();

    public LetteringGateAndSeriesProgressTests()
    {
        _series = DomainSeries.Create(_studio.Id, "Moon", ReadingDirection.RightToLeft);
        _chapter = Chapter.Create(_series.Id, 1);
        _page = Page.CreateNew(_chapter.Id, 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        _panel = Panel.Create(_page.Id, 1, 1);
        _db.StudioWorkspaces.Add(_studio);
        _db.Series.Add(_series);
        _db.Chapters.Add(_chapter);
        _db.Pages.Add(_page);
        _db.Panels.Add(_panel);
        _repo.Setup(r => r.GetAsync(_page.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_page);
        _db.SaveChanges();
    }

    private async Task<ScriptLine> AnchoredLineAsync(string? dialogue)
    {
        var scene = Scene.Create(_chapter.Id, 1);
        var line = ScriptLine.Create(scene.Id, 1, dialogue, stageDirection: dialogue is null ? "Cảnh mưa" : null);
        line.AssignToPanel(_panel.Id);
        _db.Scenes.Add(scene);
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();
        return line;
    }

    private Assignment LetteringJob() => Assignment.Create(
        _series.Id, _chapter.Id, WorkflowEntityType.Page, _page.Id, Guid.NewGuid(), _letterer, WorkspaceRole.Letterer, "Đặt chữ");

    private Task<string?> GateAsync() =>
        StageProgressRules.LetteringBlockReasonAsync(_db, _repo.Object, LetteringJob(), [_panel], CancellationToken.None);

    [Fact]
    public async Task LetteringGate_BlocksWhenAnchoredDialogueHasNoBalloon()
    {
        await AnchoredLineAsync("Đợi tôi với!");

        (await GateAsync()).Should().Contain("1 dòng thoại").And.Contain("chưa có balloon");
    }

    [Fact]
    public async Task LetteringGate_PassesOnceBalloonIsBound_AndIgnoresStageDirections()
    {
        var line = await AnchoredLineAsync("Đợi tôi với!");
        await AnchoredLineAsync(null); // chỉ dẫn cảnh, không cần balloon

        var added = await new AddElementCommandHandler(_repo.Object, _db).Handle(
            new AddElementCommand(_page.Id, "SPEECH_BALLOON", 0, 0, 400, 300, 1, null, null, Guid.NewGuid(), line.Id),
            CancellationToken.None);
        added.IsSuccess.Should().BeTrue();

        (await GateAsync()).Should().BeNull();
    }

    [Fact]
    public async Task LetteringGate_IgnoresLinesNotAnchoredToThisPage()
    {
        var scene = Scene.Create(_chapter.Id, 2);
        _db.Scenes.Add(scene);
        _db.ScriptLines.Add(ScriptLine.Create(scene.Id, 1, "Chưa neo vào panel nào")); // PanelId null
        await _db.SaveChangesAsync();

        (await GateAsync()).Should().BeNull();
    }

    [Fact]
    public async Task SeriesProgress_AggregatesStagesWorkloadOverdueAndBurndown()
    {
        var pipeline = PipelineDefinition.Create(_studio.Id, "P", seriesId: _series.Id);
        var pencil = PipelineStage.Create(pipeline.Id, "Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
        var ink = PipelineStage.Create(pipeline.Id, "Ink", "ink", 2, allowedRole: WorkspaceRole.Artist);
        var panel2 = Panel.Create(_page.Id, 2, 2);
        _db.PipelineDefinitions.Add(pipeline);
        _db.PipelineStages.AddRange(pencil, ink);
        _db.Panels.Add(panel2);

        var editor = Guid.NewGuid();
        // Panel 1: Pencil và Ink đã duyệt; Panel 2: Pencil mới bắt đầu
        var p1 = StageProgress.Begin(_panel.Id, pencil.Id); p1.Submit(editor); p1.OpenReview(); p1.Approve();
        var p2 = StageProgress.Begin(panel2.Id, pencil.Id);
        var inkRow = StageProgress.Begin(_panel.Id, ink.Id); inkRow.Submit(editor); inkRow.OpenReview(); inkRow.Approve();
        _db.StageProgresses.AddRange(p1, p2, inkRow);

        var artist = PanelForge.Domain.Entities.Identity.User.Create("a@x.com", "Chloe Artist", "hash");
        _db.Users.Add(artist);
        var done = Assignment.Create(_series.Id, _chapter.Id, WorkflowEntityType.Page, _page.Id, pencil.Id, artist.Id, WorkspaceRole.Artist, "Vẽ");
        done.StartWork(); done.SubmitForReview(); done.OpenReview(); done.MarkApproved();
        var late = Assignment.Create(_series.Id, _chapter.Id, WorkflowEntityType.Page, _page.Id, ink.Id, artist.Id, WorkspaceRole.Artist,
            "Đi nét", dueDate: DateTime.UtcNow.AddDays(-2));
        _db.Assignments.AddRange(done, late);
        await _db.SaveChangesAsync();

        var result = await new GetSeriesProgressQueryHandler(_db).Handle(new GetSeriesProgressQuery(_series.Id), default);

        result.IsSuccess.Should().BeTrue();
        var chapter = result.Value!.Chapters.Should().ContainSingle().Subject;
        chapter.PanelCount.Should().Be(2);
        chapter.PercentComplete.Should().Be(50, "1/2 panel đã Approved ở stage cuối");
        var pencilRow = chapter.Stages.Single(s => s.Name == "Pencil");
        (pencilRow.Approved, pencilRow.InProgress, pencilRow.NotStarted).Should().Be((1, 1, 0));
        chapter.Stages.Single(s => s.Name == "Ink").NotStarted.Should().Be(1);
        chapter.OverdueAssignments.Should().Be(1);

        var load = result.Value.Workload.Should().ContainSingle().Subject;
        (load.FullName, load.Open, load.Overdue, load.Done).Should().Be(("Chloe Artist", 1, 1, 1));
        result.Value.Overdue.Should().ContainSingle().Which.AssignmentId.Should().Be(late.Id);
        result.Value.Burndown.Last().Remaining.Should().Be(1, "2 việc đã tạo, 1 đã Approved");
    }

    [Fact]
    public async Task SeriesProgress_UnknownSeries_ReturnsNotFound()
    {
        var result = await new GetSeriesProgressQueryHandler(_db).Handle(new GetSeriesProgressQuery(Guid.NewGuid()), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(PanelForge.Application.Common.ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task SeriesProgress_IncludesOverdueChangeRequests()
    {
        var artist = User.Create("chloe2@test.com", "Chloe Two", role: SystemRole.User);
        _db.Users.Add(artist);
        var asg = Assignment.Create(_series.Id, _chapter.Id, WorkflowEntityType.Page, _page.Id, Guid.NewGuid(), artist.Id, WorkspaceRole.Artist, "Vẽ");
        var overdueCr = ChangeRequest.Create(asg.Id, _chapter.Id, null, artist.Id, Guid.NewGuid(), "Sửa màu mắt", dueDate: DateTime.UtcNow.AddDays(-1));
        var futureCr = ChangeRequest.Create(asg.Id, _chapter.Id, null, artist.Id, Guid.NewGuid(), "Sửa tóc", dueDate: DateTime.UtcNow.AddDays(2));
        _db.Assignments.Add(asg);
        _db.ChangeRequests.AddRange(overdueCr, futureCr);
        await _db.SaveChangesAsync();

        var result = await new GetSeriesProgressQueryHandler(_db).Handle(new GetSeriesProgressQuery(_series.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OverdueChangeRequests.Should().NotBeNull();
        result.Value.OverdueChangeRequests!.Should().ContainSingle(cr =>
            cr.ChangeRequestId == overdueCr.Id &&
            cr.ResponsibleUserId == artist.Id &&
            cr.ResponsibleUserName == "Chloe Two" &&
            cr.Description == "Sửa màu mắt");
        result.Value.Chapters.Single(c => c.ChapterId == _chapter.Id).OverdueChangeRequests.Should().Be(1);
    }
}
