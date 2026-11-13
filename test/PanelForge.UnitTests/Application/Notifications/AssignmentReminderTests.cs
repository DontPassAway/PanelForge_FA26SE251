using FluentAssertions;
using PanelForge.Application.Features.Notifications.Commands.SendAssignmentReminders;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Notifications;

/// <summary>UC-14: nhắc người nhận việc sắp đến hạn / quá hạn, một lần cho mỗi hạn, không nhắc việc đã xong hoặc chương đã khoá.</summary>
public class AssignmentReminderTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lead = TimeSpan.FromHours(24);

    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Guid _artist = Guid.NewGuid();

    public AssignmentReminderTests()
    {
        var studio = StudioWorkspace.Create("Studio", Guid.NewGuid());
        _series = DomainSeries.Create(studio.Id, "Moon", ReadingDirection.RightToLeft);
        _chapter = Chapter.Create(_series.Id, 1).InProduction();
        _db.StudioWorkspaces.Add(studio);
        _db.Series.Add(_series);
        _db.Chapters.Add(_chapter);
        _db.SaveChanges();
    }

    private Assignment Job(DateTime? due, Chapter? chapter = null)
    {
        var c = chapter ?? _chapter;
        var a = Assignment.Create(c.SeriesId, c.Id, WorkflowEntityType.Chapter, c.Id, Guid.NewGuid(),
            _artist, WorkspaceRole.Artist, "Vẽ trang 1", dueDate: due);
        _db.Assignments.Add(a);
        _db.SaveChanges();
        return a;
    }

    private Task<int> RunAsync(DateTime? at = null) =>
        new SendAssignmentRemindersCommandHandler(_db).Handle(new SendAssignmentRemindersCommand(Lead, at ?? Now), default);

    [Fact]
    public async Task DueWithinLead_NotifiesAssigneeOnce()
    {
        var a = Job(Now.AddHours(5));

        (await RunAsync()).Should().Be(1);
        (await RunAsync()).Should().Be(0, "đã nhắc cho hạn này rồi");

        _db.Notifications.Should().ContainSingle(n =>
            n.UserId == _artist && n.AssignmentId == a.Id && n.Type == NotificationType.AssignmentDueSoon && n.ChapterId == _chapter.Id);
    }

    [Fact]
    public async Task DueBeyondLead_IsNotRemindedYet()
    {
        Job(Now.AddHours(30));

        (await RunAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Overdue_NotifiesOnce_AndIgnoresVeryOldOnes()
    {
        var recent = Job(Now.AddHours(-3));
        Job(Now.AddDays(-30));

        (await RunAsync()).Should().Be(1);
        (await RunAsync()).Should().Be(0);

        _db.Notifications.Should().ContainSingle(n => n.AssignmentId == recent.Id && n.Type == NotificationType.AssignmentOverdue);
    }

    [Fact]
    public async Task DueSoonThenOverdue_AreTwoSeparateReminders()
    {
        Job(Now.AddHours(2));
        await RunAsync();

        (await RunAsync(Now.AddHours(3))).Should().Be(1);

        _db.Notifications.Select(n => n.Type).ToList().Should()
            .BeEquivalentTo([NotificationType.AssignmentDueSoon, NotificationType.AssignmentOverdue]);
    }

    [Fact]
    public async Task ChangedDueDate_RemindsAgain()
    {
        var a = Job(Now.AddHours(5));
        await RunAsync();

        a.UpdateBriefAndDeadline(a.Brief, a.ReferenceNotes, null, Now.AddHours(8), a.Priority);
        await _db.SaveChangesAsync();

        (await RunAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(AssignmentStatus.Submitted)]
    [InlineData(AssignmentStatus.UnderReview)]
    [InlineData(AssignmentStatus.Approved)]
    public async Task FinishedOrWaitingForReview_IsNotReminded(AssignmentStatus status)
    {
        var a = Job(Now.AddHours(2));
        a.StartWork();
        a.SubmitForReview();
        if (status >= AssignmentStatus.UnderReview) a.OpenReview();
        if (status == AssignmentStatus.Approved) a.MarkApproved();
        await _db.SaveChangesAsync();

        (await RunAsync()).Should().Be(0);
    }

    [Fact]
    public async Task NoDueDate_OrLockedChapter_IsNotReminded()
    {
        Job(null);
        var approved = Chapter.Create(_series.Id, 2).InProduction();
        _db.Chapters.Add(approved);
        await _db.SaveChangesAsync();
        Job(Now.AddHours(2), approved);
        approved.Approve();
        await _db.SaveChangesAsync();

        (await RunAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ChangeRequest_DueWithinLead_NotifiesResponsibleUserOnce()
    {
        var cr = ChangeRequest.Create(Guid.NewGuid(), _chapter.Id, null, _artist, Guid.NewGuid(),
            "Vẽ lại mắt nhân vật", dueDate: Now.AddHours(5));
        _db.ChangeRequests.Add(cr);
        await _db.SaveChangesAsync();

        (await RunAsync()).Should().Be(1);
        (await RunAsync()).Should().Be(0, "đã nhắc cho hạn yêu cầu sửa này rồi");

        _db.Notifications.Should().ContainSingle(n =>
            n.UserId == _artist && n.AssignmentId == cr.AssignmentId && n.Type == NotificationType.ChangeRequestDueSoon && n.ChapterId == _chapter.Id);
    }

    [Fact]
    public async Task ChangeRequest_Overdue_NotifiesResponsibleUserOnce()
    {
        var cr = ChangeRequest.Create(Guid.NewGuid(), _chapter.Id, null, _artist, Guid.NewGuid(),
            "Chỉnh sửa bubble thoại trang 3", dueDate: Now.AddHours(-4));
        _db.ChangeRequests.Add(cr);
        await _db.SaveChangesAsync();

        (await RunAsync()).Should().Be(1);
        (await RunAsync()).Should().Be(0);

        _db.Notifications.Should().ContainSingle(n =>
            n.UserId == _artist && n.AssignmentId == cr.AssignmentId && n.Type == NotificationType.ChangeRequestOverdue && n.ChapterId == _chapter.Id);
    }

    [Fact]
    public async Task ChangeRequest_ResolvedOrVerified_IsNotReminded()
    {
        var cr = ChangeRequest.Create(Guid.NewGuid(), _chapter.Id, null, _artist, Guid.NewGuid(),
            "Sửa màu nền", dueDate: Now.AddHours(-2));
        cr.Resolve("Đã đổi sang màu xanh");
        _db.ChangeRequests.Add(cr);
        await _db.SaveChangesAsync();

        (await RunAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ManualRun_OnlyRemindsWorkspacesWhereCallerIsProducer()
    {
        Job(Now.AddHours(5));
        var producer = Guid.NewGuid();
        var writer = Guid.NewGuid();
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(_series.WorkspaceId, producer, WorkspaceRole.Producer));
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(_series.WorkspaceId, writer, WorkspaceRole.Writer));
        await _db.SaveChangesAsync();

        Task<int> RunAs(Guid user) =>
            new SendAssignmentRemindersCommandHandler(_db).Handle(new SendAssignmentRemindersCommand(Lead, Now, user), default);

        (await RunAs(writer)).Should().Be(0, "không phải Producer của workspace này");
        (await RunAs(Guid.NewGuid())).Should().Be(0, "không thuộc workspace");
        (await RunAs(producer)).Should().Be(1);
    }
}
