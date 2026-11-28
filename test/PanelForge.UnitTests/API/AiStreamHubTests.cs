using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Moq;
using PanelForge.API.Hubs;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Queries.CanJoinRunStream;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.API;

/// <summary>
/// Issue #26: hub AI phát output từng bước AI của nội dung chưa phát hành, nên phải đăng nhập và chỉ thành viên Series
/// của run mới vào được group (BR-07, BR-23).
/// </summary>
public class AiStreamHubTests
{
    [Fact]
    public void Hub_RequiresAuthentication_AndHasNoSeriesGroups()
    {
        typeof(AiStreamHub).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        typeof(AiStreamHub).GetMethod("JoinSeries").Should().BeNull("không ai phát vào group series_*, không mở đường nghe lén");
    }

    [Fact]
    public void RunGroup_MatchesLowercaseGuidFormatUsedByPublisher()
    {
        var id = Guid.Parse("ABCDEF00-0000-0000-0000-000000000001");
        AiStreamHub.RunGroup(id).Should().Be("run_abcdef00-0000-0000-0000-000000000001");
    }

    [Fact]
    public async Task JoinRun_Member_IsAddedToRunGroup()
    {
        var userId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var (hub, groups) = NewHub(userId, allowed: true);

        await hub.JoinRun(runId.ToString().ToUpperInvariant());

        groups.Verify(g => g.AddToGroupAsync("conn-1", AiStreamHub.RunGroup(runId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinRun_NotAllowed_ThrowsAndDoesNotJoin()
    {
        var (hub, groups) = NewHub(Guid.NewGuid(), allowed: false);

        var act = () => hub.JoinRun(Guid.NewGuid().ToString());

        await act.Should().ThrowAsync<HubException>();
        groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JoinRun_InvalidRunId_Throws()
    {
        var (hub, groups) = NewHub(Guid.NewGuid(), allowed: true);

        var act = () => hub.JoinRun("series-or-garbage");

        await act.Should().ThrowAsync<HubException>();
        groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CanJoin_SeriesMember_True_NonMember_False_UnknownRun_False()
    {
        var db = InMemoryDb.Create();
        var seriesId = Guid.NewGuid();
        var run = AiWorkflowRun.Create(Guid.NewGuid(), seriesId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Gemini", "m",
            "brief", ReadingDirection.RightToLeft, 2, 0, null, "{}", "[]");
        db.AiWorkflowRuns.Add(run);
        await db.SaveChangesAsync();

        var member = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        var auth = new Mock<IWorkspaceAuthorizationService>();
        auth.Setup(a => a.GetSeriesMemberRoleAsync(member, seriesId, It.IsAny<CancellationToken>())).ReturnsAsync(WorkspaceRole.Writer);
        auth.Setup(a => a.GetSeriesMemberRoleAsync(outsider, seriesId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkspaceRole?)null);
        var handler = new CanJoinRunStreamQueryHandler(db, auth.Object);

        (await handler.Handle(new CanJoinRunStreamQuery(run.Id, member), default)).Should().BeTrue();
        (await handler.Handle(new CanJoinRunStreamQuery(run.Id, outsider), default)).Should().BeFalse();
        (await handler.Handle(new CanJoinRunStreamQuery(Guid.NewGuid(), member), default)).Should().BeFalse();
        (await handler.Handle(new CanJoinRunStreamQuery(run.Id, Guid.Empty), default)).Should().BeFalse();
    }

    private static (AiStreamHub Hub, Mock<IGroupManager> Groups) NewHub(Guid userId, bool allowed)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.Is<CanJoinRunStreamQuery>(q => q.UserId == userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed);

        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("conn-1");
        context.SetupGet(c => c.ConnectionAborted).Returns(CancellationToken.None);
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")));

        var groups = new Mock<IGroupManager>();
        var hub = new AiStreamHub(sender.Object) { Context = context.Object, Groups = groups.Object };
        return (hub, groups);
    }
}
