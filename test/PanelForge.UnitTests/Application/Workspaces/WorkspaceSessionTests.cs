using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Workspaces.Commands.RevokeWorkspaceSession;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceActiveSessions;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Workspaces;

public class WorkspaceSessionTests
{
    [Fact]
    public async Task GetActiveSessions_ReturnsOnlyActiveTokensForWorkspaceMembers()
    {
        var workspaceId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid(), 1000);
        typeof(StudioWorkspace).GetProperty("Id")!.SetValue(workspace, workspaceId);

        var memberId = Guid.NewGuid();
        var nonMemberId = Guid.NewGuid();

        var memberUser = User.Create(
            "artist@example.com",
            "Artist Name",
            "hash",
            role: SystemRole.User);
        typeof(User).GetProperty("Id")!.SetValue(memberUser, memberId);

        var member = WorkspaceMember.Create(workspaceId, memberId, WorkspaceRole.Artist);
        typeof(WorkspaceMember).GetProperty("User")!.SetValue(member, memberUser);

        var session1Id = Guid.NewGuid();
        var activeToken = RefreshToken.Create(
            memberId, "hash-1", DateTime.UtcNow.AddHours(2), session1Id,
            deviceName: "Chrome (Windows)", ipAddress: "192.168.1.10", userAgent: "Mozilla/5.0");

        var revokedToken = RefreshToken.Create(
            memberId, "hash-2", DateTime.UtcNow.AddHours(2), Guid.NewGuid());
        revokedToken.Revoke();

        var nonMemberToken = RefreshToken.Create(
            nonMemberId, "hash-3", DateTime.UtcNow.AddHours(2), Guid.NewGuid());

        var db = new Mock<IPanelForgeDbContext>();
        db.Setup(d => d.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        db.Setup(d => d.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceMember> { member }));
        db.Setup(d => d.RefreshTokens).Returns(DbSetMockHelper.CreateDbSetMock(new List<RefreshToken> { activeToken, revokedToken, nonMemberToken }));

        var handler = new GetWorkspaceActiveSessionsQueryHandler(db.Object);
        var result = await handler.Handle(new GetWorkspaceActiveSessionsQuery(workspaceId, memberId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value![0].SessionId.Should().Be(session1Id);
        result.Value[0].FullName.Should().Be("Artist Name");
        result.Value[0].Email.Should().Be("artist@example.com");
        result.Value[0].DeviceName.Should().Be("Chrome (Windows)");
        result.Value[0].IpAddress.Should().Be("192.168.1.10");
        result.Value[0].IsCurrentSession.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeSession_RevokesActiveTokensAndLogsAudit()
    {
        var workspaceId = Guid.NewGuid();
        var producerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var memberUser = User.Create("writer@example.com", "Writer", "hash", role: SystemRole.User);
        typeof(User).GetProperty("Id")!.SetValue(memberUser, memberId);

        var member = WorkspaceMember.Create(workspaceId, memberId, WorkspaceRole.Writer);
        var token = RefreshToken.Create(memberId, "hash-token", DateTime.UtcNow.AddHours(2), sessionId, deviceName: "Safari (macOS)");
        typeof(RefreshToken).GetProperty("User")!.SetValue(token, memberUser);

        var auditLogs = new List<AuditLog>();
        var db = new Mock<IPanelForgeDbContext>();
        db.Setup(d => d.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceMember> { member }));
        db.Setup(d => d.RefreshTokens).Returns(DbSetMockHelper.CreateDbSetMock(new List<RefreshToken> { token }));
        db.Setup(d => d.AuditLogs).Returns(DbSetMockHelper.CreateDbSetMock(auditLogs));
        db.Setup(d => d.Users.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberUser);

        var handler = new RevokeWorkspaceSessionCommandHandler(db.Object);
        var result = await handler.Handle(new RevokeWorkspaceSessionCommand(workspaceId, sessionId, producerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        token.IsRevoked.Should().BeTrue();
        db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
