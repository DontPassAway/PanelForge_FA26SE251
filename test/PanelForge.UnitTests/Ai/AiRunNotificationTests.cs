using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>BR-02, UC-14: người yêu cầu được báo khi run kết thúc cần xem; run bị gián đoạn thì không báo.</summary>
public class AiRunNotificationTests
{
    private readonly Guid _requester = Guid.NewGuid();
    private readonly Guid _chapterId = Guid.NewGuid();
    private readonly List<Notification> _notifications = [];
    private readonly Mock<IPanelForgeDbContext> _db = new();

    public AiRunNotificationTests()
    {
        _db.Setup(d => d.Notifications).Returns(DbSetMockHelper.CreateDbSetMock(_notifications));
    }

    private AiWorkflowRun NewRun() => AiWorkflowRun.Create(
        Guid.NewGuid(), Guid.NewGuid(), _chapterId, null, _requester,
        "gemini", "model", "brief", ReadingDirection.RightToLeft, 2, 0, null, "{}", "[]",
        AiWorkflowType.Copilot);

    [Fact]
    public void Completed_NotifiesRequesterWithDedicatedType()
    {
        var run = NewRun();
        run.MarkCompleted();

        AiRunHelpers.NotifyRequesterOfFinish(_db.Object, run);

        _notifications.Should().ContainSingle(n =>
            n.UserId == _requester && n.Type == NotificationType.AiRunCompleted && n.ChapterId == _chapterId);
    }

    [Fact]
    public void WaitingForHuman_And_Failed_NotifyRequester()
    {
        var waiting = NewRun();
        waiting.MarkWaitingForHuman();
        AiRunHelpers.NotifyRequesterOfFinish(_db.Object, waiting);

        var failed = NewRun();
        failed.MarkFailed("AI_UNAVAILABLE", null);
        AiRunHelpers.NotifyRequesterOfFinish(_db.Object, failed);

        _notifications.Select(n => n.Type).Should().BeEquivalentTo(
            new[] { NotificationType.AiRunWaitingForHuman, NotificationType.AiRunFailed });
    }

    [Fact]
    public void Interrupted_DoesNotNotify()
    {
        var run = NewRun();
        run.MarkInterrupted("AI service không còn hoạt động.");

        AiRunHelpers.NotifyRequesterOfFinish(_db.Object, run);

        _notifications.Should().BeEmpty();
    }
}
