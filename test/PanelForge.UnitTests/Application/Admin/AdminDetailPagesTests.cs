using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Admin.Queries.GetAdminUserDetail;
using PanelForge.Application.Features.Admin.Queries.GetAdminWorkspaceDetail;
using PanelForge.Application.Features.Admin.Queries.GetSystemHealth;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Admin;

/// <summary>OPS-30 chi tiết User, OPS-31 chi tiết Studio, OPS-32 System Health (UC-01, UC-15, BR-07, BR-18).</summary>
public class AdminDetailPagesTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public async Task UserDetail_ReturnsOnlyLiveSessions_MembershipsWalletAndActivity_OPS30()
    {
        await using var db = InMemoryDb.Create();
        var producer = User.Create("p@test.com", "Producer");
        var ws = StudioWorkspace.Create("Studio A", producer.Id, 5 * Gb);
        db.Users.Add(producer);
        db.StudioWorkspaces.Add(ws);
        db.WorkspaceMembers.Add(WorkspaceMember.Create(ws.Id, producer.Id, WorkspaceRole.Producer));
        var now = DateTime.UtcNow;
        db.RefreshTokens.Add(RefreshToken.Create(producer.Id, "live", now.AddDays(7), deviceName: "Chrome", lastActiveAt: now));
        var revoked = RefreshToken.Create(producer.Id, "old", now.AddDays(7), deviceName: "Firefox", lastActiveAt: now);
        revoked.Revoke();
        db.RefreshTokens.Add(revoked);
        db.ReaderWallets.Add(ReaderWallet.Create(producer.Id, 50_000m));
        db.AiUsageRecords.Add(AiUsageRecord.Create(ws.Id, "copilot", "Gemini", AiUsageStatus.Succeeded, producer.Id, promptTokens: 100, completionTokens: 50));
        db.AiUsageRecords.Add(AiUsageRecord.Create(ws.Id, "copilot", "Gemini", AiUsageStatus.ProviderUnavailable, producer.Id));
        db.AuditLogs.Add(AuditLog.Create("LOGIN", "User", producer.Id.ToString(), userId: producer.Id, userEmail: producer.Email));
        await db.SaveChangesAsync();

        var result = await new GetAdminUserDetailQueryHandler(db)
            .Handle(new GetAdminUserDetailQuery(producer.Id), CancellationToken.None);

        var dto = result.Value!;
        dto.ActiveSessions.Should().ContainSingle().Which.DeviceName.Should().Be("Chrome");
        dto.Memberships.Should().ContainSingle().Which.Should().Match<AdminUserMembershipDto>(m => m.IsOwner && m.WorkspaceName == "Studio A");
        dto.Wallet!.BalanceVnd.Should().Be(50_000m);
        dto.AiUsageThisMonth.Should().Be(new AdminUserAiUsageDto(150, 2, 1));
        dto.RecentActivity.Should().Contain(a => a.Action == "LOGIN");
    }

    [Fact]
    public async Task UserDetail_UnknownUser_IsNotFound_OPS30()
    {
        await using var db = InMemoryDb.Create();

        var result = await new GetAdminUserDetailQueryHandler(db)
            .Handle(new GetAdminUserDetailQuery(Guid.NewGuid()), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task WorkspaceDetail_ReturnsResourcesMembersAndSeriesCounts_OPS31()
    {
        await using var db = InMemoryDb.Create();
        var producer = User.Create("p@test.com", "Producer");
        var writer = User.Create("w@test.com", "Writer");
        var ws = StudioWorkspace.Create("Studio A", producer.Id, 5 * Gb);
        var series = Series.Create(ws.Id, "Truyện 1", ReadingDirection.RightToLeft);
        db.Users.AddRange(producer, writer);
        db.StudioWorkspaces.Add(ws);
        db.WorkspaceMembers.AddRange(
            WorkspaceMember.Create(ws.Id, producer.Id, WorkspaceRole.Producer),
            WorkspaceMember.Create(ws.Id, writer.Id, WorkspaceRole.Writer));
        db.Series.Add(series);
        db.Chapters.AddRange(Chapter.Create(series.Id, 1), Chapter.Create(series.Id, 2));
        db.WorkspaceAiConfigs.Add(WorkspaceAiConfig.Create(ws.Id, monthlyTokenQuota: 1_000));
        db.QuotaIncreaseRequests.Add(QuotaIncreaseRequest.Submit(ws.Id, QuotaResourceType.Storage, 5 * Gb, 10 * Gb, "Thêm chương", producer.Id));
        await db.SaveChangesAsync();

        var result = await new GetAdminWorkspaceDetailQueryHandler(db, new WorkspaceDefaultsSettings())
            .Handle(new GetAdminWorkspaceDetailQuery(ws.Id), CancellationToken.None);

        var dto = result.Value!;
        dto.Resources.Name.Should().Be("Studio A");
        dto.Resources.PendingRequestCount.Should().Be(1);
        dto.Members.Select(m => m.Role).Should().BeEquivalentTo([WorkspaceRole.Producer, WorkspaceRole.Writer]);
        dto.Series.Should().ContainSingle().Which.ChapterCount.Should().Be(2);
        dto.QuotaRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task WorkspaceDetail_UnknownStudio_IsNotFound_OPS31()
    {
        await using var db = InMemoryDb.Create();

        var result = await new GetAdminWorkspaceDetailQueryHandler(db, new WorkspaceDefaultsSettings())
            .Handle(new GetAdminWorkspaceDetailQuery(Guid.NewGuid()), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task SystemHealth_WorstDependencyWins_AndCountsPendingQuotaRequests_OPS32()
    {
        await using var db = InMemoryDb.Create();
        var ws = StudioWorkspace.Create("Studio A", Guid.NewGuid(), 5 * Gb);
        db.StudioWorkspaces.Add(ws);
        db.QuotaIncreaseRequests.Add(QuotaIncreaseRequest.Submit(ws.Id, QuotaResourceType.Storage, 5 * Gb, 10 * Gb, "Thêm chương", Guid.NewGuid()));
        await db.SaveChangesAsync();
        var probe = new FakeProbe(
            new DependencyHealthDto("database", HealthStatus.Healthy, "PostgreSQL", 3, null, null),
            new DependencyHealthDto("ai", HealthStatus.Degraded, "PanelForge.AI", 5, "SERVICE_KEY_MISSING", 0));

        var dto = (await new GetSystemHealthQueryHandler(db, probe).Handle(new GetSystemHealthQuery(), CancellationToken.None)).Value!;

        dto.Status.Should().Be(HealthStatus.Degraded);
        var pending = dto.Signals.Single(s => s.Key == "pendingQuotaRequests");
        pending.Count.Should().Be(1);
        pending.Warning.Should().BeFalse("yêu cầu vừa gửi chưa quá 3 ngày");
        dto.Signals.Single(s => s.Key == "aiRunsStuck").Count.Should().Be(0);
    }

    [Fact]
    public async Task SystemHealth_DatabaseDown_ReturnsDependenciesWithoutSignals_OPS32()
    {
        await using var db = InMemoryDb.Create();
        var probe = new FakeProbe(new DependencyHealthDto("database", HealthStatus.Down, "PostgreSQL", null, "UNREACHABLE", null));

        var dto = (await new GetSystemHealthQueryHandler(db, probe).Handle(new GetSystemHealthQuery(), CancellationToken.None)).Value!;

        dto.Status.Should().Be(HealthStatus.Down);
        dto.Signals.Should().BeEmpty();
    }

    private sealed class FakeProbe(params DependencyHealthDto[] results) : ISystemHealthProbe
    {
        public Task<IReadOnlyList<DependencyHealthDto>> CheckDependenciesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DependencyHealthDto>>(results);
    }
}
