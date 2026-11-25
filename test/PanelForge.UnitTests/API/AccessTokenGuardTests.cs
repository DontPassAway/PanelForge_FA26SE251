using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.API.Authentication;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.API;

/// <summary>Access JWT mất hiệu lực ngay khi phiên của nó bị thu hồi / hết hạn / quá idle timeout hoặc user bị khoá.</summary>
public class AccessTokenGuardTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly User _user = User.Create("artist@example.com", "Artist", "hash");
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;

    public AccessTokenGuardTests()
    {
        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    private async Task<bool> IsValid(Guid? sessionId, params RefreshToken[] tokens)
    {
        _db.RefreshTokens.AddRange(tokens);
        await _db.SaveChangesAsync();
        return await AccessTokenGuard.IsValidAsync(_db, _user.Id, sessionId, _now, CancellationToken.None);
    }

    private RefreshToken Token(Guid? userId = null, Guid? sessionId = null, int expiresInHours = 2, DateTime? lastActiveAt = null)
        => RefreshToken.Create(userId ?? _user.Id, Guid.NewGuid().ToString(), _now.AddHours(expiresInHours), sessionId ?? _sessionId,
            lastActiveAt: lastActiveAt ?? _now);

    private void JoinWorkspace(int idleMinutes)
    {
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid());
        workspace.SetSessionPolicy(null, idleMinutes, null);
        _db.StudioWorkspaces.Add(workspace);
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(workspace.Id, _user.Id, WorkspaceRole.Artist));
        _db.SaveChanges();
    }

    private Task<RefreshToken> StoredToken() => _db.RefreshTokens.AsNoTracking().SingleAsync();

    [Fact]
    public async Task ActiveSession_IsValid()
        => (await IsValid(_sessionId, Token())).Should().BeTrue();

    [Fact]
    public async Task RevokedSession_IsRejected()
    {
        var token = Token();
        token.Revoke();

        (await IsValid(_sessionId, token)).Should().BeFalse();
    }

    [Fact]
    public async Task RotatedSession_StaysValid_WhileOneTokenIsActive()
    {
        var old = Token();
        old.Revoke(); // refresh xoay vòng: token cũ thu hồi, token mới cùng phiên

        (await IsValid(_sessionId, old, Token())).Should().BeTrue();
    }

    [Fact]
    public async Task ExpiredSession_IsRejected()
        => (await IsValid(_sessionId, Token(expiresInHours: -1))).Should().BeFalse();

    [Fact]
    public async Task SessionOfAnotherUser_IsRejected()
        => (await IsValid(_sessionId, Token(userId: Guid.NewGuid()))).Should().BeFalse();

    [Fact]
    public async Task TokenWithoutSessionClaim_OnlyChecksUser()
        => (await IsValid(null)).Should().BeTrue();

    [Fact]
    public async Task InactiveUser_IsRejected_EvenWithActiveSession()
    {
        _user.Deactivate();

        (await IsValid(_sessionId, Token())).Should().BeFalse();
    }

    [Fact]
    public async Task SessionIdleLongerThanPolicy_IsRejected_BeforeTheAccessTokenExpires()
    {
        JoinWorkspace(idleMinutes: 10);

        (await IsValid(_sessionId, Token(lastActiveAt: _now.AddMinutes(-11)))).Should().BeFalse();
    }

    [Fact]
    public async Task ActiveSession_RecordsActivity_WithoutWritingAnAuditLogPerRequest()
    {
        JoinWorkspace(idleMinutes: 10);

        (await IsValid(_sessionId, Token(lastActiveAt: _now.AddMinutes(-5)))).Should().BeTrue();

        (await StoredToken()).LastActiveAt.Should().Be(_now);
        (await _db.AuditLogs.CountAsync(a => a.EntityName == nameof(RefreshToken) && a.Action.StartsWith("UPDATE")))
            .Should().Be(0, "nhịp hoạt động của phiên không phải thao tác nghiệp vụ");
    }

    [Fact]
    public async Task ActivityWithinWriteInterval_IsNotRewritten()
    {
        var lastActive = _now - AccessTokenGuard.ActivityWriteInterval / 2;

        (await IsValid(_sessionId, Token(lastActiveAt: lastActive))).Should().BeTrue();

        (await StoredToken()).LastActiveAt.Should().Be(lastActive);
    }
}
