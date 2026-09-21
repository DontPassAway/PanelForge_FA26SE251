using FluentAssertions;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class AssignmentTests
{
    [Fact]
    public void Assignment_Create_ValidParameters_ShouldInstantiateWithAssignedStatus()
    {
        // Arrange
        var seriesId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var panelId = Guid.NewGuid();
        var stageId = Guid.NewGuid();
        var artistId = Guid.NewGuid();
        var dueDate = DateTime.UtcNow.AddDays(3);

        // Act
        var assignment = Assignment.Create(
            seriesId,
            chapterId,
            WorkflowEntityType.Panel,
            panelId,
            stageId,
            artistId,
            WorkspaceRole.Penciler,
            "Draw dynamic splash perspective for explosion scene",
            "See Series Bible LOC-01 for district architecture",
            "[\"https://storage/ref1.png\"]",
            dueDate,
            AssignmentPriority.High);

        // Assert
        assignment.Should().NotBeNull();
        assignment.SeriesId.Should().Be(seriesId);
        assignment.ChapterId.Should().Be(chapterId);
        assignment.EntityType.Should().Be(WorkflowEntityType.Panel);
        assignment.TargetEntityId.Should().Be(panelId);
        assignment.PipelineStageId.Should().Be(stageId);
        assignment.AssigneeUserId.Should().Be(artistId);
        assignment.AssignedRole.Should().Be(WorkspaceRole.Penciler);
        assignment.Brief.Should().Be("Draw dynamic splash perspective for explosion scene");
        assignment.ReferenceNotes.Should().Be("See Series Bible LOC-01 for district architecture");
        assignment.ReferenceAssetUrlsJson.Should().Be("[\"https://storage/ref1.png\"]");
        assignment.DueDate.Should().Be(dueDate);
        assignment.Priority.Should().Be(AssignmentPriority.High);
        assignment.Status.Should().Be(AssignmentStatus.Assigned);
        assignment.StartedAt.Should().BeNull();
        assignment.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Assignment_Lifecycle_ShouldFollowStateTransitions()
    {
        // Arrange
        var assignment = Assignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkflowEntityType.Page,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkspaceRole.Inker,
            "Inking page 12");

        // 1. Start Work
        assignment.StartWork();
        assignment.Status.Should().Be(AssignmentStatus.InProgress);
        assignment.StartedAt.Should().NotBeNull();

        // 2. Submit for Review
        assignment.SubmitForReview();
        assignment.Status.Should().Be(AssignmentStatus.Submitted);

        // 3. Request Rework (Editor rejects with comment)
        assignment.RequestRework("Linework on character jawline is inconsistent with character sheet CHR-01");
        assignment.Status.Should().Be(AssignmentStatus.ReworkRequired);
        assignment.LastFeedbackComment.Should().Be("Linework on character jawline is inconsistent with character sheet CHR-01");

        // 4. Artist resumes work from ReworkRequired
        assignment.StartWork();
        assignment.Status.Should().Be(AssignmentStatus.InProgress);

        // 5. Re-submit and Approve
        assignment.SubmitForReview();
        assignment.MarkApproved();

        assignment.Status.Should().Be(AssignmentStatus.Approved);
        assignment.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Assignment_InvalidStateTransitions_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var assignment = Assignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkflowEntityType.Page,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkspaceRole.Letterer,
            "Place dialogue balloons on page 5");

        // Act & Assert: Cannot submit before starting (if not in assigned/in-progress)
        assignment.StartWork();
        assignment.SubmitForReview();
        assignment.MarkApproved();

        // Cannot start work when already Approved
        Action actStart = () => assignment.StartWork();
        actStart.Should().Throw<InvalidOperationException>();

        // Cannot submit when already Approved
        Action actSubmit = () => assignment.SubmitForReview();
        actSubmit.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Assignment_Reassign_ShouldUpdateAssigneeAndRole()
    {
        // Arrange
        var initialUserId = Guid.NewGuid();
        var assignment = Assignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkflowEntityType.Page,
            Guid.NewGuid(),
            Guid.NewGuid(),
            initialUserId,
            WorkspaceRole.Penciler,
            "Rough sketches");

        var newUserId = Guid.NewGuid();

        // Act
        assignment.Reassign(newUserId, WorkspaceRole.Inker);

        // Assert
        assignment.AssigneeUserId.Should().Be(newUserId);
        assignment.AssignedRole.Should().Be(WorkspaceRole.Inker);
    }

    [Fact]
    public void Assignment_UpdateBriefAndDeadline_ShouldUpdateDetailsCorrectly()
    {
        // Arrange
        var assignment = Assignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkflowEntityType.Panel,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkspaceRole.Writer,
            "Initial scene draft");

        var newDeadline = DateTime.UtcNow.AddDays(5);

        // Act
        assignment.UpdateBriefAndDeadline(
            "Updated climax scene with deeper dialogue",
            "Refer to plot fact FACT-01",
            "[\"https://storage/storyboard.jpg\"]",
            newDeadline,
            AssignmentPriority.Urgent);

        // Assert
        assignment.Brief.Should().Be("Updated climax scene with deeper dialogue");
        assignment.ReferenceNotes.Should().Be("Refer to plot fact FACT-01");
        assignment.ReferenceAssetUrlsJson.Should().Be("[\"https://storage/storyboard.jpg\"]");
        assignment.DueDate.Should().Be(newDeadline);
        assignment.Priority.Should().Be(AssignmentPriority.Urgent);
    }
}
