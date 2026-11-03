using FluentAssertions;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Models;
using PanelForge.Domain.Services;
using Xunit;

namespace PanelForge.UnitTests.Domain;

/// <summary>
/// Unit Tests for Core Production Architecture:
/// Series (Parent Workspace & Bible) -> Chapter (Production Container) -> Script (Dedicated Chapter Document).
/// </summary>
public class ChapterProductionArchitectureTests
{
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly Guid _unauthorizedUserId = Guid.NewGuid();

    [Fact]
    public void DomainHierarchy_SeriesToChapterToScript_ShouldEncapsulateCorrectly()
    {
        // 1. Series Level
        var series = Series.Create(
            workspaceId: Guid.NewGuid(),
            title: "Neon Samurai Chronicles",
            readingDirection: ReadingDirection.RightToLeft,
            synopsis: "Cyberpunk manga about ronin in futuristic Neo-Kyoto.");

        // 2. Chapter Level
        var chapter = Chapter.Create(
            seriesId: series.Id,
            chapterNumber: 1,
            title: "Chapter 1: The Spark",
            targetReleaseDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));

        // 3. Script Level (Strictly encapsulated inside Chapter)
        var script = ChapterScript.Create(chapter.Id, "EXT. NEO-KYOTO - NIGHT\nRain pours over neon signs.");
        chapter.AttachScript(script);

        // Assertions
        chapter.Script.Should().NotBeNull();
        chapter.Script!.ChapterId.Should().Be(chapter.Id);
        chapter.Script.Version.Should().Be(1);
        chapter.Script.IsLocked.Should().BeFalse();
        chapter.Script.RawText.Should().Contain("EXT. NEO-KYOTO - NIGHT");
    }

    [Fact]
    public void ChapterStateMachine_FullProgression_ShouldLockScriptOnApproval()
    {
        var chapterId = Guid.NewGuid();
        var script = ChapterScript.Create(chapterId, "Initial Script Content");

        var status = ProductionChapterStatus.Planning;

        // 1. Planning -> Scripting (Producer initiates)
        ChapterProductionStateMachine.CanTransition(status, ProductionChapterStatus.Scripting).Should().BeTrue();
        ChapterProductionStateMachine.Transition(
            script, status, ProductionChapterStatus.Scripting,
            actorUserId: _producerId, actorRole: ProjectRoleInProject.Producer);
        status = ProductionChapterStatus.Scripting;
        script.IsLocked.Should().BeFalse();

        // 2. Scripting: Writer updates script
        ProductionAccessGuard.CanEditScript(script, status, _writerId, _producerId, _writerId, out _).Should().BeTrue();
        script.UpdateRawText("Updated Script by Writer", _writerId);
        script.Version.Should().Be(2);

        // 3. Scripting -> InReview (Writer submits)
        ChapterProductionStateMachine.CanTransition(status, ProductionChapterStatus.InReview).Should().BeTrue();
        ChapterProductionStateMachine.Transition(
            script, status, ProductionChapterStatus.InReview,
            actorUserId: _writerId, actorRole: ProjectRoleInProject.Writer,
            assignedWriterId: _writerId);
        status = ProductionChapterStatus.InReview;

        // 4. InReview -> Approved (Producer approves: Script MUST lock automatically)
        ChapterProductionStateMachine.CanTransition(status, ProductionChapterStatus.Approved).Should().BeTrue();
        ChapterProductionStateMachine.Transition(
            script, status, ProductionChapterStatus.Approved,
            actorUserId: _producerId, actorRole: ProjectRoleInProject.Producer,
            assignedProducerId: _producerId);
        status = ProductionChapterStatus.Approved;

        // Verify narrative baseline is frozen
        script.IsLocked.Should().BeTrue();
        script.LockedAt.Should().NotBeNull();
        script.LockedById.Should().Be(_producerId);

        // Edits are strictly forbidden once locked
        var actEdit = () => script.UpdateRawText("Late edit attempt", _writerId);
        actEdit.Should().Throw<InvalidOperationException>()
            .WithMessage("*locked and frozen*");

        // 5. Approved -> Storyboarding (Artist phase)
        ChapterProductionStateMachine.CanTransition(status, ProductionChapterStatus.Storyboarding).Should().BeTrue();
        ChapterProductionStateMachine.Transition(
            script, status, ProductionChapterStatus.Storyboarding,
            actorUserId: _producerId, actorRole: ProjectRoleInProject.Producer);
        status = ProductionChapterStatus.Storyboarding;

        // 6. Storyboarding -> Drawing
        ChapterProductionStateMachine.CanTransition(status, ProductionChapterStatus.Drawing).Should().BeTrue();
        ChapterProductionStateMachine.Transition(
            script, status, ProductionChapterStatus.Drawing,
            actorUserId: _producerId, actorRole: ProjectRoleInProject.Producer);
        status = ProductionChapterStatus.Drawing;

        // 7. Drawing -> Published
        ChapterProductionStateMachine.CanTransition(status, ProductionChapterStatus.Published).Should().BeTrue();
        ChapterProductionStateMachine.Transition(
            script, status, ProductionChapterStatus.Published,
            actorUserId: _producerId, actorRole: ProjectRoleInProject.Producer);
        status = ProductionChapterStatus.Published;

        // Published is terminal
        ChapterProductionStateMachine.CanTransition(status, ProductionChapterStatus.Scripting).Should().BeFalse();
    }

    [Fact]
    public void ChapterStateMachine_RevisionLoop_ShouldAllowReturningToScripting()
    {
        var chapterId = Guid.NewGuid();
        var script = ChapterScript.Create(chapterId, "Draft 1");

        // Move to InReview
        var status = ProductionChapterStatus.Scripting;
        ChapterProductionStateMachine.Transition(
            script, status, ProductionChapterStatus.InReview,
            actorUserId: _writerId, actorRole: ProjectRoleInProject.Writer,
            assignedWriterId: _writerId);
        status = ProductionChapterStatus.InReview;

        // Producer rejects / requests revision: returns to Scripting
        ChapterProductionStateMachine.CanTransition(status, ProductionChapterStatus.Scripting).Should().BeTrue();
        ChapterProductionStateMachine.Transition(
            script, status, ProductionChapterStatus.Scripting,
            actorUserId: _producerId, actorRole: ProjectRoleInProject.Producer);
        status = ProductionChapterStatus.Scripting;

        // Writer can edit again
        script.IsLocked.Should().BeFalse();
        var editAllowed = ProductionAccessGuard.CanEditScript(
            script, status, _writerId, _producerId, _writerId, out var reason);
        editAllowed.Should().BeTrue();
        reason.Should().BeNull();

        script.UpdateRawText("Draft 2 with revisions", _writerId);
        script.RawText.Should().Be("Draft 2 with revisions");
    }

    [Theory]
    [InlineData(ProductionChapterStatus.Planning, ProductionChapterStatus.Approved)]
    [InlineData(ProductionChapterStatus.Planning, ProductionChapterStatus.Drawing)]
    [InlineData(ProductionChapterStatus.Scripting, ProductionChapterStatus.Storyboarding)]
    [InlineData(ProductionChapterStatus.Approved, ProductionChapterStatus.Planning)]
    [InlineData(ProductionChapterStatus.Published, ProductionChapterStatus.Scripting)]
    public void ChapterStateMachine_InvalidTransitions_ShouldThrowInvalidOperationException(
        ProductionChapterStatus from, ProductionChapterStatus to)
    {
        var script = ChapterScript.Create(Guid.NewGuid(), "Sample");

        ChapterProductionStateMachine.CanTransition(from, to).Should().BeFalse();

        var act = () => ChapterProductionStateMachine.Transition(
            script, from, to, _producerId, ProjectRoleInProject.Producer);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*Invalid production state transition from '{from}' to '{to}'*");
    }

    [Fact]
    public void ProductionAccessGuard_UnauthorizedUser_ShouldBeRejected()
    {
        var script = ChapterScript.Create(Guid.NewGuid(), "Script Content");
        var status = ProductionChapterStatus.Scripting;

        // Artist attempts to edit script
        var actArtist = () => ProductionAccessGuard.EnsureCanEditScript(
            script, status, _artistId, _producerId, _writerId);
        actArtist.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*Only the assigned Writer or the Producer is authorized*");

        // Random stranger attempts to edit script
        var actStranger = () => ProductionAccessGuard.EnsureCanEditScript(
            script, status, _unauthorizedUserId, _producerId, _writerId);
        actStranger.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*Only the assigned Writer or the Producer is authorized*");
    }

    [Fact]
    public void ProductionAccessGuard_WhenLockedOrNotInScripting_ShouldBeBlocked()
    {
        var script = ChapterScript.Create(Guid.NewGuid(), "Script Content");

        // 1. Wrong status (InReview)
        var actReview = () => ProductionAccessGuard.EnsureCanEditScript(
            script, ProductionChapterStatus.InReview, _writerId, _producerId, _writerId);
        actReview.Should().Throw<InvalidOperationException>()
            .WithMessage("*only permitted when Chapter status is 'Scripting'*");

        // 2. Locked script
        script.Lock(_producerId);
        var actLocked = () => ProductionAccessGuard.EnsureCanEditScript(
            script, ProductionChapterStatus.Scripting, _writerId, _producerId, _writerId);
        actLocked.Should().Throw<InvalidOperationException>()
            .WithMessage("*Script is locked and cannot be edited*");
    }

    [Fact]
    public void IntraPageSceneTransition_MultipleScenesOnSinglePage_ShouldDetectAndAnalyzeTransitions()
    {
        var presentSceneId = Guid.NewGuid();
        var flashbackSceneId = Guid.NewGuid();

        // 1 Page with 3 Panels:
        // Panel 1: Present Scene
        // Panel 2: Flashback Scene (Scene Transition)
        // Panel 3: Flashback Scene
        var pagePanels = new List<(int PanelNumber, Guid SceneId, bool IsFlashback)>
        {
            (1, presentSceneId, false),
            (2, flashbackSceneId, true),
            (3, flashbackSceneId, true)
        };

        var analysis = IntraPageSceneAnalysis.Analyze(pagePanels);

        analysis.IsMultiScene.Should().BeTrue();
        analysis.SceneIds.Should().HaveCount(2);
        analysis.SceneIds.Should().Contain(presentSceneId);
        analysis.SceneIds.Should().Contain(flashbackSceneId);

        analysis.Transitions.Should().HaveCount(1);
        var transition = analysis.Transitions[0];
        transition.FromSceneId.Should().Be(presentSceneId);
        transition.ToSceneId.Should().Be(flashbackSceneId);
        transition.AtPanelNumber.Should().Be(2);
    }
}
