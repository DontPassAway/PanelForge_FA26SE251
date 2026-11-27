using FluentAssertions;
using PanelForge.Application.Features.Notifications.Commands.SendAssignmentReminders;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Notifications;

/// <summary>Assign Automation – Bước 5: R3 (giờ nhắc theo Studio, leo thang, ô bị ngâm) và G3 (nhắc chốt chương).</summary>
public class AutomationReminderTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly StudioWorkspace _studio;
    private readonly Chapter _chapter;
    private readonly Guid _artist = Guid.NewGuid();
    private readonly Guid _assigner = Guid.NewGuid();
    private readonly Guid _stageId = Guid.NewGuid();

    public AutomationReminderTests()
    {
        _studio = StudioWorkspace.Create("Studio", Guid.NewGuid());
        var series = DomainSeries.Create(_studio.Id, "Moon", ReadingDirection.RightToLeft);
        _chapter = Chapter.Create(series.Id, 1).InProduction();
        _db.AddRange(_studio, series, _chapter);
        _db.SaveChanges();
    }

    private void Settings(int lead = 24, int? escalation = null, bool autoNext = false)
    {
        var s = WorkspaceAutomationSettings.Create(_studio.Id);
        s.Update(autoNext, AutoAssignMode.KeepPageOwner, false, lead, escalation);
        _db.WorkspaceAutomationSettings.Add(s);
        _db.SaveChanges();
    }

    private (StageTaskLot Lot, StageTask Cell) OpenLot(DateTime? due)
    {
        var lot = StageTaskLot.Create(_chapter.SeriesId, _chapter.Id, _stageId, _artist, WorkspaceRole.Artist, _assigner, null, due);
        var cell = StageTask.Create(_chapter.SeriesId, _chapter.Id, _stageId, Guid.NewGuid());
        cell.AssignTo(lot.Id, prerequisitesApproved: true);
        _db.AddRange(lot, cell);
        _db.SaveChanges();
        return (lot, cell);
    }

    private Task<int> Run(DateTime now) =>
        new SendAssignmentRemindersCommandHandler(_db).Handle(new SendAssignmentRemindersCommand(TimeSpan.FromHours(24), now), default);

    [Fact]
    public async Task StudioLeadHours_ReplaceDefaultLead()
    {
        var now = DateTime.UtcNow;
        OpenLot(now.AddHours(30));
        (await Run(now)).Should().Be(0, "mặc định 24 giờ");

        Settings(lead: 48);
        (await Run(now)).Should().Be(1);
        _db.Notifications.Should().ContainSingle(n => n.UserId == _artist && n.Type == NotificationType.AssignmentDueSoon);
    }

    [Fact]
    public async Task OverdueBeyondEscalation_AlsoTellsOwner_Once()
    {
        Settings(escalation: 12);
        var now = DateTime.UtcNow;
        OpenLot(now.AddHours(-13));

        (await Run(now)).Should().Be(3, "người làm + người giao + Owner");
        (await Run(now)).Should().Be(0);
        _db.Notifications.Should().ContainSingle(n => n.UserId == _studio.OwnerId && n.Title.Contains("12 giờ"));
    }

    [Fact]
    public async Task OverdueWithinEscalation_DoesNotEscalate()
    {
        Settings(escalation: 12);
        var now = DateTime.UtcNow;
        OpenLot(now.AddHours(-3));

        await Run(now);
        _db.Notifications.Should().NotContain(n => n.UserId == _studio.OwnerId);
    }

    [Fact]
    public async Task StaleReadyCell_TellsOwner_OnlyWhenEscalationSet()
    {
        OpenLot(null);
        var later = DateTime.UtcNow.AddHours(13);

        (await Run(later)).Should().Be(0, "chưa đặt escalation_hours");

        Settings(escalation: 12);
        (await Run(later)).Should().Be(1);
        (await Run(later)).Should().Be(0, "nội dung cố định nên không báo lặp");
        _db.Notifications.Should().ContainSingle(n => n.UserId == _studio.OwnerId && n.Title.Contains("bị ngâm"));
    }

    [Fact]
    public async Task G3_NudgesOwnerOnce_WhenEveryCellApproved_AndAutomationOn()
    {
        _db.Pages.Add(Page.CreateNew(_chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300));
        var cell = StageTask.Create(_chapter.SeriesId, _chapter.Id, _stageId, null);
        _db.StageTasks.Add(cell);
        _db.SaveChanges();

        Settings(autoNext: true);
        await TaskGridAutomation.NudgeChapterLockAsync(_db, _chapter, default);
        _db.Notifications.Should().BeEmpty("còn ô chưa duyệt");

        var lot = Guid.NewGuid();
        cell.AssignTo(lot, true);
        cell.Start(true);
        cell.Submit(null, isApprovalGate: false);
        _db.SaveChanges();

        await TaskGridAutomation.NudgeChapterLockAsync(_db, _chapter, default);
        await TaskGridAutomation.NudgeChapterLockAsync(_db, _chapter, default);
        _db.Notifications.Should().ContainSingle(n => n.UserId == _studio.OwnerId && n.Title.Contains("sẵn sàng chốt"));
    }

    [Fact]
    public async Task G3_DoesNothing_WhenAutomationOff()
    {
        _db.Pages.Add(Page.CreateNew(_chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300));
        _db.SaveChanges();

        await TaskGridAutomation.NudgeChapterLockAsync(_db, _chapter, default);
        _db.Notifications.Should().BeEmpty();
    }
}