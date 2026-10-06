using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Notifications.Commands.MarkNotificationsRead;
using PanelForge.Application.Features.Notifications.Queries.GetMyNotifications;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Notifications;

/// <summary>UC-14: người dùng chỉ thấy và đánh dấu đọc thông báo của chính mình.</summary>
public class NotificationTests
{
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly List<Notification> _rows = [];
    private readonly Mock<IPanelForgeDbContext> _db = new();

    public NotificationTests()
    {
        _db.Setup(d => d.Notifications).Returns(DbSetMockHelper.CreateDbSetMock(_rows));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private Notification Add(Guid user, string title = "t")
    {
        var n = Notification.Create(user, NotificationType.AssignmentAssigned, title, "nội dung");
        _rows.Add(n);
        return n;
    }

    [Fact]
    public async Task List_ReturnsOnlyMine_WithUnreadCount()
    {
        Add(_me, "a");
        Add(_me, "b").MarkRead();
        Add(_other, "của người khác");

        var result = await new GetMyNotificationsQueryHandler(_db.Object)
            .Handle(new GetMyNotificationsQuery(_me), CancellationToken.None);

        result.Value!.TotalCount.Should().Be(2);
        result.Value.UnreadCount.Should().Be(1);
        result.Value.Items.Select(i => i.Title).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task List_UnreadOnly_FiltersReadOnes_AndClampsPageSize()
    {
        Add(_me, "a");
        Add(_me, "b").MarkRead();

        var result = await new GetMyNotificationsQueryHandler(_db.Object)
            .Handle(new GetMyNotificationsQuery(_me, UnreadOnly: true, PageIndex: 0, PageSize: 1000), CancellationToken.None);

        result.Value!.Items.Should().ContainSingle().Which.Title.Should().Be("a");
        result.Value.TotalCount.Should().Be(1);
        result.Value.PageIndex.Should().Be(1);
        result.Value.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task MarkOne_Mine_SetsReadAt()
    {
        var n = Add(_me);

        var result = await new MarkNotificationsReadCommandHandler(_db.Object)
            .Handle(new MarkNotificationsReadCommand(_me, n.Id), CancellationToken.None);

        result.Value.Should().Be(1);
        n.ReadAt.Should().NotBeNull();
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkOne_BelongingToSomeoneElse_IsNotFound_AndUntouched()
    {
        var theirs = Add(_other);

        var result = await new MarkNotificationsReadCommandHandler(_db.Object)
            .Handle(new MarkNotificationsReadCommand(_me, theirs.Id), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        theirs.ReadAt.Should().BeNull();
    }

    [Fact]
    public async Task MarkAll_MarksOnlyMyUnread()
    {
        var a = Add(_me);
        var b = Add(_me);
        var theirs = Add(_other);

        var result = await new MarkNotificationsReadCommandHandler(_db.Object)
            .Handle(new MarkNotificationsReadCommand(_me), CancellationToken.None);

        result.Value.Should().Be(2);
        a.ReadAt.Should().NotBeNull();
        b.ReadAt.Should().NotBeNull();
        theirs.ReadAt.Should().BeNull();
    }

    [Fact]
    public async Task MarkAll_NothingUnread_DoesNotSave()
    {
        Add(_me).MarkRead();

        var result = await new MarkNotificationsReadCommandHandler(_db.Object)
            .Handle(new MarkNotificationsReadCommand(_me), CancellationToken.None);

        result.Value.Should().Be(0);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void MarkRead_IsIdempotent_KeepsFirstReadTime()
    {
        var n = Add(_me);
        n.MarkRead();
        var first = n.ReadAt;

        n.MarkRead();

        n.ReadAt.Should().Be(first);
    }
}
