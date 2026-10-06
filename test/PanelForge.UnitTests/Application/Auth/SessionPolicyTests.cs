using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Auth.Commands.Login;
using PanelForge.Application.Features.Auth.Commands.RefreshToken;
using PanelForge.Application.Features.Workspaces.Commands.UpdateWorkspaceSessionPolicy;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceSessionPolicy;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using Token = PanelForge.Domain.Entities.Identity.RefreshToken;

namespace PanelForge.UnitTests.Application.Auth;

/// <summary>V1.2 (CF1): session policy theo Workspace (thời gian sống, idle timeout, số phiên đồng thời) áp ở đăng nhập và làm mới token.</summary>
public class SessionPolicyTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IJwtTokenGenerator> _jwt = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly User _user;

    public SessionPolicyTests()
    {
        _user = User.Create("user@example.com", "User", "hash");
        _user.ConfirmEmail();
        _db.Users.Add(_user);
        _db.SaveChanges();
        _hasher.Setup(h => h.VerifyPassword("Password123!", "hash")).Returns(true);
        _jwt.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns(("jwt", DateTime.UtcNow.AddMinutes(30)));
    }

    private StudioWorkspace JoinWorkspace(int? lifetime = null, int? idle = null, int? maxSessions = null)
    {
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid());
        workspace.SetSessionPolicy(lifetime, idle, maxSessions);
        _db.StudioWorkspaces.Add(workspace);
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(workspace.Id, _user.Id, WorkspaceRole.Writer));
        _db.SaveChanges();
        return workspace;
    }

    private async Task<string> LoginAsync()
    {
        var result = await new LoginCommandHandler(_db, _hasher.Object, _jwt.Object)
            .Handle(new LoginCommand("user@example.com", "Password123!", null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        return result.Value!.RefreshToken!;
    }

    private Task<Result<PanelForge.Application.Features.Auth.Models.AuthResponse>> RefreshAsync(string token) =>
        new RefreshTokenCommandHandler(_db, _jwt.Object).Handle(new RefreshTokenCommand(token), CancellationToken.None);

    private static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private async Task<Token> AddTokenAsync(string raw, DateTime sessionStartedAt, DateTime issuedAt)
    {
        var token = Token.Create(_user.Id, Hash(raw), DateTime.UtcNow.AddDays(1), sessionStartedAt: sessionStartedAt);
        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync();
        // CreatedAt bị ghi đè khi thêm mới; lùi lại sau khi lưu để giả lập lần làm mới gần nhất đã lâu.
        typeof(Token).GetProperty("CreatedAt")!.SetValue(token, issuedAt);
        _db.Entry(token).Property(t => t.CreatedAt).IsModified = true;
        await _db.SaveChangesAsync();
        return token;
    }

    [Fact]
    public async Task Login_WithMaxConcurrentSessions_RevokesTheOldestSession()
    {
        JoinWorkspace(maxSessions: 2);

        await LoginAsync();
        await Task.Delay(20);
        await LoginAsync();
        await Task.Delay(20);
        await LoginAsync();

        var tokens = await _db.RefreshTokens.OrderBy(t => t.SessionStartedAt).ToListAsync();
        tokens.Should().HaveCount(3);
        tokens[0].IsRevoked.Should().BeTrue("phiên cũ nhất bị thu hồi khi vượt giới hạn");
        tokens[1].IsRevoked.Should().BeFalse();
        tokens[2].IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Login_WithoutWorkspacePolicy_DoesNotRevokeAnything()
    {
        JoinWorkspace();

        await LoginAsync();
        await LoginAsync();
        await LoginAsync();

        (await _db.RefreshTokens.CountAsync(t => t.RevokedAt == null)).Should().Be(3);
    }

    [Fact]
    public async Task Login_WithSessionLifetime_CapsRefreshTokenExpiry()
    {
        JoinWorkspace(lifetime: 60);

        await LoginAsync();

        var token = await _db.RefreshTokens.SingleAsync();
        token.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(60), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Login_UserInSeveralWorkspaces_GetsTheStrictestLifetime()
    {
        JoinWorkspace(lifetime: 120);
        JoinWorkspace(lifetime: 30);
        JoinWorkspace();

        await LoginAsync();

        var token = await _db.RefreshTokens.SingleAsync();
        token.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Refresh_WhenSessionOlderThanLifetime_IsRejected_AndDoesNotRevokeOtherSessions()
    {
        JoinWorkspace(lifetime: 60);
        await AddTokenAsync("old-session", sessionStartedAt: DateTime.UtcNow.AddHours(-2), issuedAt: DateTime.UtcNow.AddMinutes(-1));
        var other = await AddTokenAsync("other-session", sessionStartedAt: DateTime.UtcNow.AddMinutes(-5), issuedAt: DateTime.UtcNow.AddMinutes(-1));

        var result = await RefreshAsync("old-session");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        other.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_WhenIdleLongerThanTimeout_IsRejected_ButWithinTimeoutRotatesAndKeepsTheSession()
    {
        JoinWorkspace(idle: 10);
        var idleToken = await AddTokenAsync("idle", sessionStartedAt: DateTime.UtcNow.AddHours(-1), issuedAt: DateTime.UtcNow.AddMinutes(-30));
        var activeToken = await AddTokenAsync("active", sessionStartedAt: DateTime.UtcNow.AddHours(-1), issuedAt: DateTime.UtcNow.AddMinutes(-2));

        (await RefreshAsync("idle")).IsSuccess.Should().BeFalse();
        idleToken.IsRevoked.Should().BeFalse();

        var rotated = await RefreshAsync("active");

        rotated.IsSuccess.Should().BeTrue(rotated.ErrorMessage);
        activeToken.IsRevoked.Should().BeTrue();
        var next = await _db.RefreshTokens.SingleAsync(t => t.TokenHash == Hash(rotated.Value!.RefreshToken!));
        next.SessionId.Should().Be(activeToken.SessionId);
        next.SessionStartedAt.Should().Be(activeToken.SessionStartedAt);
    }

    [Fact]
    public async Task AdminUpdatesPolicy_ThenReadsItBack_AndInvalidValuesAreRejected()
    {
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid());
        _db.StudioWorkspaces.Add(workspace);
        await _db.SaveChangesAsync();
        var update = new UpdateWorkspaceSessionPolicyCommandHandler(_db);

        var ok = await update.Handle(new UpdateWorkspaceSessionPolicyCommand(workspace.Id, 480, 30, 3), CancellationToken.None);
        var read = await new GetWorkspaceSessionPolicyQueryHandler(_db)
            .Handle(new GetWorkspaceSessionPolicyQuery(workspace.Id), CancellationToken.None);
        var idleAboveLifetime = await update.Handle(new UpdateWorkspaceSessionPolicyCommand(workspace.Id, 60, 120, null), CancellationToken.None);
        var tooShort = await update.Handle(new UpdateWorkspaceSessionPolicyCommand(workspace.Id, 1, null, null), CancellationToken.None);
        var missing = await update.Handle(new UpdateWorkspaceSessionPolicyCommand(Guid.NewGuid(), null, null, null), CancellationToken.None);
        var cleared = await update.Handle(new UpdateWorkspaceSessionPolicyCommand(workspace.Id, null, null, null), CancellationToken.None);

        ok.IsSuccess.Should().BeTrue(ok.ErrorMessage);
        read.Value.Should().Be(new PanelForge.Application.Features.Workspaces.Models.WorkspaceSessionPolicyDto(workspace.Id, 480, 30, 3));
        idleAboveLifetime.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        tooShort.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        missing.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        cleared.Value!.SessionLifetimeMinutes.Should().BeNull();
        cleared.Value.MaxConcurrentSessions.Should().BeNull();
    }
}
