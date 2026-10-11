using FluentAssertions;
using Moq;
using PanelForge.API.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Identity;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.API;

/// <summary>Access JWT mất hiệu lực ngay khi phiên của nó bị thu hồi / hết hạn hoặc user bị khoá.</summary>
public class AccessTokenGuardTests
{
    private readonly User _user = User.Create("artist@example.com", "Artist", "hash");
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;

    private Task<bool> IsValid(Guid? sessionId, params RefreshToken[] tokens)
    {
        var db = new Mock<IPanelForgeDbContext>();
        db.Setup(d => d.Users).Returns(DbSetMockHelper.CreateDbSetMock(new List<User> { _user }));
        db.Setup(d => d.RefreshTokens).Returns(DbSetMockHelper.CreateDbSetMock(tokens.ToList()));
        return AccessTokenGuard.IsValidAsync(db.Object, _user.Id, sessionId, _now, CancellationToken.None);
    }

    private RefreshToken Token(Guid? userId = null, Guid? sessionId = null, int expiresInHours = 2)
        => RefreshToken.Create(userId ?? _user.Id, Guid.NewGuid().ToString(), _now.AddHours(expiresInHours), sessionId ?? _sessionId);

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
}
