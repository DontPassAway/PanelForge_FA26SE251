using FluentAssertions;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

/// <summary>Assign Automation – Bước 1: bất biến ReadyAt (N4) và dọn gợi ý người duyệt (N5) trên StageTask.</summary>
public class StageTaskReadyAtTests
{
    private static StageTask NewCell() => StageTask.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void AssignTo_SetsReadyAt_OnlyWhenPrerequisitesApproved()
    {
        var ready = NewCell();
        ready.AssignTo(Guid.NewGuid(), prerequisitesApproved: true);
        ready.ReadyAt.Should().NotBeNull();

        var waiting = NewCell();
        waiting.AssignTo(Guid.NewGuid(), prerequisitesApproved: false);
        waiting.ReadyAt.Should().BeNull();
    }

    [Fact]
    public void MarkReady_SetsOnlyOnce_ClearReady_Resets_AndUnassignedCellIsNeverReady()
    {
        var cell = NewCell();
        cell.MarkReady();
        cell.ReadyAt.Should().BeNull("chưa có người nhận");

        cell.AssignTo(Guid.NewGuid());
        cell.MarkReady();
        var first = cell.ReadyAt;
        first.Should().NotBeNull();
        cell.MarkReady();
        cell.ReadyAt.Should().Be(first, "chỉ ghi khi đang trống");

        cell.ClearReady();
        cell.ReadyAt.Should().BeNull();
    }

    [Fact]
    public void Start_Unassign_Cancel_ClearReadyAt()
    {
        var started = NewCell();
        started.AssignTo(Guid.NewGuid(), true);
        started.Start(true);
        started.ReadyAt.Should().BeNull();

        var unassigned = NewCell();
        unassigned.AssignTo(Guid.NewGuid(), true);
        unassigned.Unassign();
        unassigned.ReadyAt.Should().BeNull();

        var cancelled = NewCell();
        cancelled.AssignTo(Guid.NewGuid(), true);
        cancelled.Cancel();
        cancelled.ReadyAt.Should().BeNull();
    }

    [Fact]
    public void RequestChanges_SetsReadyAt_WhenPrerequisitesStillApproved()
    {
        var cell = NewCell();
        cell.AssignTo(Guid.NewGuid(), true);
        cell.Start(true);
        cell.Submit(null, isApprovalGate: true);
        cell.OpenReview(Guid.NewGuid());
        cell.RequestChanges(prerequisitesApproved: true);

        cell.Status.Should().Be(StageTaskStatus.ReworkRequired);
        cell.ReadyAt.Should().NotBeNull();
    }

    [Fact]
    public void Reopen_SetsReadyAt_MarkInputOutdated_Clears_ResolveImpactRework_SetsAgain()
    {
        var cell = NewCell();
        cell.AssignTo(Guid.NewGuid(), true);
        cell.Start(true);
        cell.Submit(null, isApprovalGate: false);
        cell.Status.Should().Be(StageTaskStatus.Approved);

        cell.Reopen("sửa lại");
        cell.ReadyAt.Should().NotBeNull();

        cell.MarkInputOutdated("Pencil được mở lại");
        cell.ReadyAt.Should().BeNull();

        cell.ResolveImpact(keep: false, prerequisitesApproved: true);
        cell.ReadyAt.Should().NotBeNull();
    }

    [Fact]
    public void Submit_ClearsReviewerOfPreviousRound()
    {
        var editor = Guid.NewGuid();
        var cell = NewCell();
        cell.AssignTo(Guid.NewGuid(), true);
        cell.Start(true);
        cell.Submit(null, true);
        cell.OpenReview(editor);
        cell.RequestChanges(true);
        cell.ReviewerUserId.Should().Be(editor);

        cell.Start(true);
        cell.Submit(null, true);
        cell.ReviewerUserId.Should().BeNull();
    }

    [Fact]
    public void SuggestReviewer_KeepsSubmitted_WithdrawClearsIt_OpenReviewOverrides()
    {
        var suggested = Guid.NewGuid();
        var other = Guid.NewGuid();
        var cell = NewCell();
        cell.AssignTo(Guid.NewGuid(), true);
        cell.Start(true);
        cell.Submit(null, true);

        cell.SuggestReviewer(suggested);
        cell.Status.Should().Be(StageTaskStatus.Submitted);
        cell.ReviewerUserId.Should().Be(suggested);

        cell.OpenReview(other);
        cell.ReviewerUserId.Should().Be(other);
        cell.ReleaseReview();

        cell.SuggestReviewer(suggested);
        cell.Withdraw();
        cell.ReviewerUserId.Should().BeNull();
        var act = () => cell.SuggestReviewer(suggested);
        act.Should().Throw<InvalidOperationException>("chỉ gợi ý cho ô Submitted");
    }

    [Fact]
    public void ClearSuggestedReviewer_RejectsUnderReview()
    {
        var cell = NewCell();
        cell.AssignTo(Guid.NewGuid(), true);
        cell.Start(true);
        cell.Submit(null, true);
        cell.OpenReview(Guid.NewGuid());

        var act = () => cell.ClearSuggestedReviewer();
        act.Should().Throw<InvalidOperationException>();
    }
}