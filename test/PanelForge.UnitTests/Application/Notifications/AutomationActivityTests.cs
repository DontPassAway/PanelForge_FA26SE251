using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.Workspaces.Queries.GetAutomationActivity;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Notifications;

/// <summary>Assign Automation: nhật ký gần đây đọc lô tự giao + thông báo bỏ qua/báo trễ, gộp thông báo gửi nhiều Producer.</summary>
public class AutomationActivityTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly StudioWorkspace _studio = StudioWorkspace.Create("Studio", Guid.NewGuid());
    private readonly Chapter _chapter;
    private readonly User _artist = User.Create("a@x.com", "Artist A");

    public AutomationActivityTests()
    {
        var series = DomainSeries.Create(_studio.Id, "Moon", ReadingDirection.RightToLeft);
        _chapter = Chapter.Create(series.Id, 1).InProduction();
        _db.AddRange(_studio, series, _chapter, _artist);
        _db.SaveChanges();
        _auth.Setup(a => a.HasWorkspaceRoleAsync(It.IsAny<Guid>(), _studio.Id, It.IsAny<WorkspaceRole[]>(), default)).ReturnsAsync(true);
    }

    private Task<Result<AutomationActivityDto>> Run() =>
        new GetAutomationActivityQueryHandler(_db, _auth.Object).Handle(new GetAutomationActivityQuery(Guid.NewGuid(), _studio.Id), default);

    [Fact]
    public async Task ListsAutoLots_SkippedAndEscalated_DedupedAcrossProducers()
    {
        var stage = PipelineStage.Create(Guid.NewGuid(), "Ink", "ink", 1, allowedRole: WorkspaceRole.Artist);
        var page = Page.CreateNew(_chapter.Id, 3, LayoutFormat.StandardPage, 1200, 1800, 300);
        var auto = StageTaskLot.Create(_chapter.SeriesId, _chapter.Id, stage.Id, _artist.Id, WorkspaceRole.Artist, Guid.NewGuid(), null, null);
        auto.MarkAutoAssigned("Giữ người đã làm trang này");
        var manual = StageTaskLot.Create(_chapter.SeriesId, _chapter.Id, stage.Id, _artist.Id, WorkspaceRole.Artist, Guid.NewGuid(), null, null);
        var cell = StageTask.Create(_chapter.SeriesId, _chapter.Id, stage.Id, page.Id);
        cell.AssignTo(auto.Id);
        _db.AddRange(stage, page, auto, manual, cell);
        foreach (var producer in new[] { Guid.NewGuid(), Guid.NewGuid() })
            _db.Notifications.Add(TaskGridNotifications.AutoAssignSkipped(producer, _chapter.Id, 1, "Không có Inker."));
        _db.Notifications.Add(TaskGridNotifications.LotEscalated(Guid.NewGuid(), auto, 12));
        _db.Notifications.Add(TaskGridNotifications.Approved(Guid.NewGuid(), _chapter.Id, "Ink"));
        _db.SaveChanges();

        var result = await Run();

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var dto = result.Value!;
        dto.Last7Days.Should().Be(new AutomationActivitySummaryDto(1, 1, 1));
        dto.Items.Should().HaveCount(3);
        var assigned = dto.Items.Single(i => i.Kind == GetAutomationActivityQueryHandler.AutoAssigned);
        assigned.StageName.Should().Be("Ink");
        assigned.PageNumbers.Should().Equal(3);
        assigned.AssigneeName.Should().Be("Artist A");
        assigned.Reason.Should().Be("Giữ người đã làm trang này");
        dto.Items.Single(i => i.Kind == GetAutomationActivityQueryHandler.Skipped).Reason.Should().Be("Không có Inker.");
    }

    [Fact]
    public async Task NonProducerOrEditor_IsForbidden()
    {
        _auth.Setup(a => a.HasWorkspaceRoleAsync(It.IsAny<Guid>(), _studio.Id, It.IsAny<WorkspaceRole[]>(), default)).ReturnsAsync(false);
        (await Run()).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }
}
