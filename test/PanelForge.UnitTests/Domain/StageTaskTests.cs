using FluentAssertions;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

/// <summary>Lưới giao việc V3 – Lát cắt 0: ô việc StageTask và luật tiền đề theo nhánh (BR-19).</summary>
public class StageTaskTests
{
    // ── Luật tiền đề theo nhánh ──

    private static readonly Guid Def = Guid.NewGuid();

    private static PipelineStage Stage(string name, int order, string? track = null, bool enabled = true)
    {
        var s = PipelineStage.Create(Def, name, name.ToLowerInvariant(), order);
        s.SetTrack(track);
        if (!enabled) s.SetEnabled(false);
        return s;
    }

    private static string[] Names(IEnumerable<PipelineStage> stages) => stages.Select(s => s.Name).OrderBy(n => n).ToArray();

    [Fact]
    public void Prerequisites_InkAndLetteringBranchAfterPencil_ReviewWaitsForBothBranchEnds()
    {
        var pencil = Stage("Pencil", 1);
        var ink = Stage("Ink", 2, "A");
        var color = Stage("Color", 3, "A");
        var lettering = Stage("Lettering", 4, "B");
        var review = Stage("Review", 5);
        var all = new[] { pencil, ink, color, lettering, review };

        Names(StageDependencies.DirectPrerequisites(ink, all)).Should().Equal("Pencil");
        Names(StageDependencies.DirectPrerequisites(color, all)).Should().Equal("Ink");
        Names(StageDependencies.DirectPrerequisites(lettering, all)).Should().Equal("Pencil");
        Names(StageDependencies.DirectPrerequisites(review, all)).Should().Equal("Color", "Lettering");
        StageDependencies.DirectPrerequisites(pencil, all).Should().BeEmpty();
    }

    [Fact]
    public void Prerequisites_DisabledColor_IsSkipped_ReviewWaitsForInkAndLettering()
    {
        var pencil = Stage("Pencil", 1);
        var ink = Stage("Ink", 2, "A");
        var color = Stage("Color", 3, "A", enabled: false);
        var lettering = Stage("Lettering", 4, "B");
        var review = Stage("Review", 5);
        var all = new[] { pencil, ink, color, lettering, review };

        Names(StageDependencies.DirectPrerequisites(review, all)).Should().Equal("Ink", "Lettering");
    }

    [Fact]
    public void Prerequisites_NoTracks_BehavesLikeLinearPipeline()
    {
        var pencil = Stage("Pencil", 1);
        var ink = Stage("Ink", 2);
        var color = Stage("Color", 3, enabled: false);
        var lettering = Stage("Lettering", 4);
        var all = new[] { pencil, ink, color, lettering };

        Names(StageDependencies.DirectPrerequisites(ink, all)).Should().Equal("Pencil");
        Names(StageDependencies.DirectPrerequisites(lettering, all)).Should().Equal("Ink");
    }

    [Fact]
    public void SetTrack_NormalizesAndRejectsTooLong()
    {
        var s = Stage("Ink", 2, " a ");
        s.Track.Should().Be("A");
        s.SetTrack("  ");
        s.Track.Should().BeNull();
        FluentActions.Invoking(() => s.SetTrack(new string('x', PipelineStage.MaxTrackLength + 1)))
            .Should().Throw<ArgumentException>();
    }

    // ── Vòng đời ô ──

    private static StageTask Started()
    {
        var t = StageTask.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        t.AssignTo(Guid.NewGuid());
        t.Start(prerequisitesApproved: true);
        return t;
    }

    private static StageTask UnderReview()
    {
        var t = Started();
        t.Submit(Guid.NewGuid(), isApprovalGate: true);
        t.OpenReview(Guid.NewGuid());
        return t;
    }

    [Fact]
    public void Start_RequiresApprovedPrerequisites()
    {
        var t = StageTask.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        t.AssignTo(Guid.NewGuid());

        FluentActions.Invoking(() => t.Start(prerequisitesApproved: false)).Should().Throw<InvalidOperationException>();
        t.Start(prerequisitesApproved: true);
        t.Status.Should().Be(StageTaskStatus.InProgress);
        t.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void Submit_WithoutApprovalGate_IsSelfApproved()
    {
        var t = Started();
        t.Submit(Guid.NewGuid(), isApprovalGate: false);
        t.Status.Should().Be(StageTaskStatus.Approved);
    }

    [Fact]
    public void Outdated_BlocksSubmitApproveAndReopen()
    {
        var inProgress = Started();
        inProgress.MarkInputOutdated("Pencil trang 4 bị mở lại");
        FluentActions.Invoking(() => inProgress.Submit(Guid.NewGuid(), true)).Should().Throw<InvalidOperationException>();

        var review = UnderReview();
        review.MarkInputOutdated("Pencil trang 4 bị mở lại");
        FluentActions.Invoking(() => review.Approve()).Should().Throw<InvalidOperationException>();

        var approved = Started();
        approved.Submit(Guid.NewGuid(), isApprovalGate: false);
        approved.MarkInputOutdated("Pencil trang 4 bị mở lại");
        FluentActions.Invoking(() => approved.Reopen()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkInputOutdated_OnTaskNotStarted_IsIgnored()
    {
        var t = StageTask.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        t.AssignTo(Guid.NewGuid());

        t.MarkInputOutdated("Pencil bị mở lại");

        t.IsInputOutdated.Should().BeFalse();
    }

    /// <summary>Lỗi chặn #1: ô UnderReview bị cờ lỗi thời từng kẹt vì Approve và Impact Review đều bị từ chối.</summary>
    [Theory]
    [InlineData(true, StageTaskStatus.UnderReview)]
    [InlineData(false, StageTaskStatus.ReworkRequired)]
    public void ResolveImpact_OnUnderReviewTask_UnblocksIt(bool keep, StageTaskStatus expected)
    {
        var t = UnderReview();
        t.MarkInputOutdated("Pencil trang 4 bị mở lại");

        FluentActions.Invoking(() => t.ResolveImpact(keep, prerequisitesApproved: false))
            .Should().Throw<InvalidOperationException>();
        t.ResolveImpact(keep, prerequisitesApproved: true);

        t.Status.Should().Be(expected);
        t.IsInputOutdated.Should().BeFalse();
        if (keep) FluentActions.Invoking(() => t.Approve()).Should().NotThrow();
        else FluentActions.Invoking(() => t.Start(prerequisitesApproved: true)).Should().NotThrow();
    }

    /// <summary>Lỗi chặn #1 (nhánh còn lại): RequestChanges không gỡ cờ thì ReworkRequired → InProgress bị chặn mãi.</summary>
    [Fact]
    public void RequestChanges_WhenPrerequisitesApproved_ClearsOutdatedSoReworkCanStart()
    {
        var t = UnderReview();
        t.MarkInputOutdated("Pencil trang 4 bị mở lại");

        t.RequestChanges(prerequisitesApproved: true);

        t.Status.Should().Be(StageTaskStatus.ReworkRequired);
        t.IsInputOutdated.Should().BeFalse();
        FluentActions.Invoking(() => t.Start(prerequisitesApproved: true)).Should().NotThrow();
    }

    [Fact]
    public void RequestChanges_WhilePrerequisiteStillOpen_KeepsFlag_ThenImpactReviewOnReworkClearsIt()
    {
        var t = UnderReview();
        t.MarkInputOutdated("Pencil trang 4 bị mở lại");

        t.RequestChanges(prerequisitesApproved: false);
        t.IsInputOutdated.Should().BeTrue();
        FluentActions.Invoking(() => t.Start(prerequisitesApproved: true)).Should().Throw<InvalidOperationException>();

        t.ResolveImpact(keep: false, prerequisitesApproved: true);
        t.Start(prerequisitesApproved: true);
        t.Status.Should().Be(StageTaskStatus.InProgress);
    }

    /// <summary>Kịch bản biên #4 của proposal: ô về Unassigned/Assigned/Cancelled phải bỏ cờ, nếu không người mới bị chặn.</summary>
    [Fact]
    public void Unassign_Transfer_And_Cancel_ClearOutdatedFlag()
    {
        var unassigned = Started();
        unassigned.MarkInputOutdated("x");
        unassigned.Unassign();
        unassigned.IsInputOutdated.Should().BeFalse();
        unassigned.AssignmentId.Should().BeNull();

        var transferred = Started();
        transferred.MarkInputOutdated("x");
        var newLot = Guid.NewGuid();
        transferred.AssignTo(newLot);
        transferred.Status.Should().Be(StageTaskStatus.Assigned);
        transferred.AssignmentId.Should().Be(newLot);
        transferred.IsInputOutdated.Should().BeFalse();

        var cancelled = Started();
        cancelled.MarkInputOutdated("x");
        cancelled.Cancel();
        cancelled.Status.Should().Be(StageTaskStatus.Cancelled);
        cancelled.IsInputOutdated.Should().BeFalse();
    }

    [Fact]
    public void Withdraw_And_ReleaseReview_MoveBack()
    {
        var t = Started();
        t.Submit(Guid.NewGuid(), isApprovalGate: true);
        t.Withdraw();
        t.Status.Should().Be(StageTaskStatus.InProgress);

        var r = UnderReview();
        r.ReleaseReview();
        r.Status.Should().Be(StageTaskStatus.Submitted);
        r.ReviewerUserId.Should().BeNull();
    }

    [Fact]
    public void AssignTo_SubmittedOrApprovedTask_IsRejected()
    {
        var t = Started();
        t.Submit(Guid.NewGuid(), isApprovalGate: true);
        FluentActions.Invoking(() => t.AssignTo(Guid.NewGuid())).Should().Throw<InvalidOperationException>();
    }
}
