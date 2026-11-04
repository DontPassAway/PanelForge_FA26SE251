using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Assignments.Commands.CreateBulkAssignment;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Application.Features.Assignments.Queries.GetAssigneeSuggestions;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Assignments;

/// <summary>Giao hàng loạt: granularity theo stage, all-or-nothing khi trùng, 1 BatchId + 1 thông báo (BR-14), dryRun không lưu.</summary>
public partial class AssignmentTests
{
    private static Task<Result<BulkAssignmentResultDto>> Bulk(
        Fixture f, Guid stageId, IReadOnlyList<Guid>? pageIds = null, bool dryRun = false, Guid? assignee = null) =>
        new CreateBulkAssignmentCommandHandler(f.Db.Object, new PassThroughUnitOfWork()).Handle(
            new CreateBulkAssignmentCommand(f.Chapter.Id, f.ProducerId, stageId, assignee ?? f.ArtistId, "Vẽ pencil",
                pageIds, null, null, AssignmentPriority.Medium, dryRun),
            CancellationToken.None);

    [Fact]
    public async Task Bulk_ArtistStage_CreatesOneAssignmentPerPage_WithOneBatchNotification()
    {
        var f = new Fixture();
        f.AddPage(2);
        f.AddPage(3);

        var result = await Bulk(f, f.ArtistStage.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Granularity.Should().Be(WorkflowEntityType.Page);
        result.Value.CreatedCount.Should().Be(3);
        result.Value.Items.Select(i => i.PageNumber).Should().Equal(1, 2, 3);
        f.Assignments.Should().HaveCount(3).And.OnlyContain(a => a.BatchId == result.Value.BatchId);
        f.Notifications.Should().ContainSingle();
        f.Db.Verify(d => d.LockChapterAsync(f.Chapter.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Bulk_ReviewStage_CreatesSingleChapterAssignment()
    {
        var f = new Fixture();
        f.AddPage(2);

        var result = await Bulk(f, f.ReviewStage.Id, assignee: f.EditorId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Granularity.Should().Be(WorkflowEntityType.Chapter);
        f.Assignments.Should().ContainSingle(a => a.EntityType == WorkflowEntityType.Chapter && a.TargetEntityId == f.Chapter.Id);
    }

    [Fact]
    public async Task Bulk_OnePageAlreadyAssigned_RollsBackWholeBatch()
    {
        var f = new Fixture();
        var page2 = f.AddPage(2);
        f.SeedFor(WorkflowEntityType.Page, page2.Id);

        var result = await Bulk(f, f.ArtistStage.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("trang 2");
        f.Assignments.Should().ContainSingle(); // chỉ việc đã seed
        f.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Bulk_DryRun_ReportsConflictsWithoutSaving()
    {
        var f = new Fixture();
        var page2 = f.AddPage(2);
        f.SeedFor(WorkflowEntityType.Page, page2.Id);

        var result = await Bulk(f, f.ArtistStage.Id, dryRun: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BatchId.Should().BeNull();
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items.Single(i => i.PageNumber == 2).ConflictReason.Should().NotBeNull();
        result.Value.Items.Single(i => i.PageNumber == 1).ConflictReason.Should().BeNull();
        f.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Bulk_SelectedPages_OnlyAssignsThose_AndRejectsForeignPage()
    {
        var f = new Fixture();
        var page2 = f.AddPage(2);
        f.AddPage(3);

        var ok = await Bulk(f, f.ArtistStage.Id, pageIds: [page2.Id]);
        ok.Value!.Items.Should().ContainSingle(i => i.TargetEntityId == page2.Id);

        var bad = await Bulk(f, f.InkStage.Id, pageIds: [Guid.NewGuid()]);
        bad.IsSuccess.Should().BeFalse();
        bad.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Bulk_NextStage_DependsOnSamePagePriorWork()
    {
        var f = new Fixture();
        var page2 = f.AddPage(2);
        var pencil1 = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        var pencil2 = f.SeedFor(WorkflowEntityType.Page, page2.Id);

        var result = await Bulk(f, f.InkStage.Id);

        result.IsSuccess.Should().BeTrue();
        f.Assignments.Single(a => a.PipelineStageId == f.InkStage.Id && a.TargetEntityId == f.Page.Id)
            .GetDependsOnAssignmentIds().Should().Equal(pencil1.Id);
        f.Assignments.Single(a => a.PipelineStageId == f.InkStage.Id && a.TargetEntityId == page2.Id)
            .GetDependsOnAssignmentIds().Should().Equal(pencil2.Id);
    }

    [Fact]
    public async Task Bulk_ChapterStage_DependsOnAllPriorStageWork()
    {
        var f = new Fixture();
        var page2 = f.AddPage(2);
        var ink1 = f.SeedFor(WorkflowEntityType.Page, f.Page.Id, f.InkStage);
        var ink2 = f.SeedFor(WorkflowEntityType.Page, page2.Id, f.InkStage);

        var result = await Bulk(f, f.ReviewStage.Id, dryRun: true, assignee: f.EditorId);

        result.Value!.Items.Single().DependsOnAssignmentIds.Should().BeEquivalentTo([ink1.Id, ink2.Id]);
    }

    [Fact]
    public async Task Suggestions_OnlyActiveMembersOfStageRole_LeastLoadedFirst()
    {
        var f = new Fixture();
        f.Db.Setup(d => d.Users).Returns(DbSetMockHelper.CreateDbSetMock(new List<User>()));
        f.SeedFor(WorkflowEntityType.Page, f.Page.Id); // ArtistId có 1 việc đang mở

        var result = await new GetAssigneeSuggestionsQueryHandler(f.Db.Object)
            .Handle(new GetAssigneeSuggestionsQuery(f.Chapter.Id, f.ArtistStage.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(s => s.UserId).Should().Equal(f.Artist2Id, f.ArtistId); // không có Pending/Rejected/Writer
        result.Value[1].ActiveAssignmentCount.Should().Be(1);
        result.Value[1].StageExperienceCount.Should().Be(1);
    }
}
