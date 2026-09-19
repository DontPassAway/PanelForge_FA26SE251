using FluentAssertions;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class WorkflowStateMachineTests
{
    [Fact]
    public void CreateDefaultMangaPipeline_ShouldInitialize8CanonicalStages()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();

        // Act
        var pipeline = PipelineDefinition.CreateDefaultMangaPipeline(workspaceId);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.WorkspaceId.Should().Be(workspaceId);
        pipeline.Stages.Should().HaveCount(8);

        var stages = pipeline.Stages.OrderBy(s => s.StageOrder).ToList();
        stages[0].Slug.Should().Be("script");
        stages[0].IsInitial.Should().BeTrue();

        stages[1].Slug.Should().Be("thumbnail");
        stages[2].Slug.Should().Be("pencil");
        stages[3].Slug.Should().Be("ink");
        stages[4].Slug.Should().Be("color");
        stages[5].Slug.Should().Be("letter");

        stages[6].Slug.Should().Be("review");
        stages[6].IsApprovalGate.Should().BeTrue();

        stages[7].Slug.Should().Be("approved");
        stages[7].IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void CanTransition_ValidForwardTransitions_ShouldAllowAuthorizedRoles()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var pipeline = PipelineDefinition.CreateDefaultMangaPipeline(workspaceId);

        var sScript = pipeline.Stages.First(s => s.Slug == "script");
        var sThumbnail = pipeline.Stages.First(s => s.Slug == "thumbnail");
        var sLetter = pipeline.Stages.First(s => s.Slug == "letter");
        var sReview = pipeline.Stages.First(s => s.Slug == "review");
        var sApproved = pipeline.Stages.First(s => s.Slug == "approved");

        // Act & Assert
        // Writer can submit script
        pipeline.CanTransition(sScript.Id, sThumbnail.Id, WorkspaceRole.Writer).Should().BeTrue();

        // Letterer can submit to review
        pipeline.CanTransition(sLetter.Id, sReview.Id, WorkspaceRole.Letterer).Should().BeTrue();

        // Editor can approve
        pipeline.CanTransition(sReview.Id, sApproved.Id, WorkspaceRole.Editor).Should().BeTrue();
    }

    [Fact]
    public void CanTransition_GuardedTransitions_ShouldBlockUnauthorizedRoles()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var pipeline = PipelineDefinition.CreateDefaultMangaPipeline(workspaceId);

        var sReview = pipeline.Stages.First(s => s.Slug == "review");
        var sApproved = pipeline.Stages.First(s => s.Slug == "approved");

        // Act & Assert
        // Penciler/Inker/Letterer CANNOT approve submission
        pipeline.CanTransition(sReview.Id, sApproved.Id, WorkspaceRole.Penciler).Should().BeFalse();
        pipeline.CanTransition(sReview.Id, sApproved.Id, WorkspaceRole.Inker).Should().BeFalse();
        pipeline.CanTransition(sReview.Id, sApproved.Id, WorkspaceRole.Letterer).Should().BeFalse();
    }

    [Fact]
    public void CanTransition_BackwardTransitions_ShouldAllowEditorToRequestRework()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var pipeline = PipelineDefinition.CreateDefaultMangaPipeline(workspaceId);

        var sReview = pipeline.Stages.First(s => s.Slug == "review");
        var sPencil = pipeline.Stages.First(s => s.Slug == "pencil");
        var sInk = pipeline.Stages.First(s => s.Slug == "ink");
        var sLetter = pipeline.Stages.First(s => s.Slug == "letter");

        // Act & Assert
        pipeline.CanTransition(sReview.Id, sPencil.Id, WorkspaceRole.Editor).Should().BeTrue();
        pipeline.CanTransition(sReview.Id, sInk.Id, WorkspaceRole.Editor).Should().BeTrue();
        pipeline.CanTransition(sReview.Id, sLetter.Id, WorkspaceRole.Editor).Should().BeTrue();

        var backwardTransitions = pipeline.Transitions.Where(t => t.IsBackwardTransition).ToList();
        backwardTransitions.Should().HaveCount(3);
        backwardTransitions.Should().OnlyContain(t => t.RequiresComment == true);
    }

    [Fact]
    public void CanTransition_UndefinedTransition_ShouldReturnFalse()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var pipeline = PipelineDefinition.CreateDefaultMangaPipeline(workspaceId);

        var sScript = pipeline.Stages.First(s => s.Slug == "script");
        var sApproved = pipeline.Stages.First(s => s.Slug == "approved");

        // Act & Assert - Cannot jump straight from Script to Approved
        pipeline.CanTransition(sScript.Id, sApproved.Id, WorkspaceRole.Writer).Should().BeFalse();
        pipeline.CanTransition(sScript.Id, sApproved.Id, WorkspaceRole.Editor).Should().BeFalse();
    }

    [Fact]
    public void Producer_ShouldAlwaysHaveOverridePermission()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var pipeline = PipelineDefinition.CreateDefaultMangaPipeline(workspaceId);

        var sReview = pipeline.Stages.First(s => s.Slug == "review");
        var sApproved = pipeline.Stages.First(s => s.Slug == "approved");

        // Act & Assert - Producer overrides any role restriction
        pipeline.CanTransition(sReview.Id, sApproved.Id, WorkspaceRole.Producer).Should().BeTrue();
    }

    [Fact]
    public void Panel_And_Page_AdvanceToStage_ShouldTrackStageCorrectly()
    {
        // Arrange
        var page = Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 2480, 3508, 300);
        var panel = Panel.Create(page.Id, 1, 1);
        var stageId = Guid.NewGuid();

        // Act
        page.AdvanceToStage(stageId);
        panel.AdvanceToStage(stageId);

        // Assert
        page.CurrentStageId.Should().Be(stageId);
        panel.CurrentStageId.Should().Be(stageId);
    }

    [Fact]
    public void WorkflowTransitionLog_Create_ShouldStoreAuditMetadata()
    {
        // Arrange
        var panelId = Guid.NewGuid();
        var fromStageId = Guid.NewGuid();
        var toStageId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        var log = WorkflowTransitionLog.Create(
            panelId,
            WorkflowEntityType.Panel,
            fromStageId,
            toStageId,
            userId,
            "Inks approved by Lead Inker");

        // Assert
        log.EntityId.Should().Be(panelId);
        log.EntityType.Should().Be(WorkflowEntityType.Panel);
        log.FromStageId.Should().Be(fromStageId);
        log.ToStageId.Should().Be(toStageId);
        log.TriggeredByUserId.Should().Be(userId);
        log.Comment.Should().Be("Inks approved by Lead Inker");
    }
}
