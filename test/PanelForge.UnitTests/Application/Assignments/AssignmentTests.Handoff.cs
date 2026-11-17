using FluentAssertions;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;

namespace PanelForge.UnitTests.Application.Assignments;

/// <summary>Bàn giao tự động Pencil ➔ Ink (stage không cổng duyệt): chỉ tự Start khi cùng người làm (BR-19, BR-14).</summary>
public partial class AssignmentTests
{
    /// <summary>Pencil không có cổng duyệt → nộp là tự Approved và chạy bàn giao sang Ink.</summary>
    private static Assignment SeedNonGatePencil(Fixture f)
    {
        var pencil = PipelineStage.Create(f.InkStage.PipelineDefinitionId, "Pencil", "pencil", 1,
            allowedRole: WorkspaceRole.Artist, isInitial: true);
        f.Db.Setup(d => d.PipelineStages).Returns(DbSetMockHelper.CreateDbSetMock(
            new List<PipelineStage> { pencil, f.InkStage, f.ReviewStage }));
        f.AddPanel(f.Page, 1);
        return f.SeedFor(WorkflowEntityType.Page, f.Page.Id, pencil);
    }

    private static Assignment SeedInk(Fixture f, Guid assignee, Guid? dependsOn = null)
    {
        var ink = Assignment.Create(f.Series.Id, f.Chapter.Id, WorkflowEntityType.Page, f.Page.Id, f.InkStage.Id,
            assignee, WorkspaceRole.Artist, "ink", dependsOnAssignmentId: dependsOn);
        f.Assignments.Add(ink);
        return ink;
    }

    [Fact]
    public async Task Handoff_SameAssignee_AutoStartsInk()
    {
        var f = new Fixture();
        var pencil = SeedNonGatePencil(f);
        var ink = SeedInk(f, f.ArtistId);
        (await Start(f, pencil, f.ArtistId)).IsSuccess.Should().BeTrue();

        (await Submit(f, pencil)).IsSuccess.Should().BeTrue();

        pencil.Status.Should().Be(AssignmentStatus.Approved);
        ink.Status.Should().Be(AssignmentStatus.InProgress);
    }

    [Fact]
    public async Task Handoff_DifferentAssignee_KeepsInkAssigned_AndNotifiesThem()
    {
        var f = new Fixture();
        var pencil = SeedNonGatePencil(f);
        var ink = SeedInk(f, f.Artist2Id);
        (await Start(f, pencil, f.ArtistId)).IsSuccess.Should().BeTrue();

        (await Submit(f, pencil)).IsSuccess.Should().BeTrue();

        ink.Status.Should().Be(AssignmentStatus.Assigned);
        f.Notifications.Should().Contain(n => n.UserId == f.Artist2Id && n.AssignmentId == ink.Id);
    }

    [Fact]
    public async Task Handoff_SameAssignee_OtherDependencyNotApproved_KeepsInkAssigned()
    {
        var f = new Fixture();
        var pencil = SeedNonGatePencil(f);
        var other = f.SeedFor(WorkflowEntityType.Chapter, f.Chapter.Id, f.ReviewStage, WorkspaceRole.Editor);
        var ink = SeedInk(f, f.ArtistId, dependsOn: other.Id);
        (await Start(f, pencil, f.ArtistId)).IsSuccess.Should().BeTrue();

        (await Submit(f, pencil)).IsSuccess.Should().BeTrue();

        ink.Status.Should().Be(AssignmentStatus.Assigned);
    }
}
