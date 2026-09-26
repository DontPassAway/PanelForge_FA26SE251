using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Features.Audit.Queries;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.UnitTests.Infrastructure.Persistence;

/// <summary>UC-15 / BR-18: audit trail ghi người thực hiện, thay đổi và đúng hành động.</summary>
public class AuditTrailTests
{
    private readonly Guid _actorId = Guid.NewGuid();
    private const string ActorEmail = "producer@test.com";

    [Fact]
    public async Task Create_ShouldRecordActor_Ip_AndNewValues()
    {
        await using var db = InMemoryDb.Create(new FakeCurrentUser(_actorId, ActorEmail, "10.0.0.5"));
        var workspace = StudioWorkspace.Create("Studio A", _actorId);

        db.StudioWorkspaces.Add(workspace);
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.SingleAsync(a => a.EntityName == nameof(StudioWorkspace));
        log.Action.Should().Be("CREATE_STUDIOWORKSPACE");
        log.UserId.Should().Be(_actorId);
        log.UserEmail.Should().Be(ActorEmail);
        log.IpAddress.Should().Be("10.0.0.5");
        log.WorkspaceId.Should().Be(workspace.Id);
        JsonDocument.Parse(log.ChangesJson!).RootElement.GetProperty("Name").GetString().Should().Be("Studio A");
        workspace.CreatedBy.Should().Be(_actorId.ToString());
    }

    [Fact]
    public async Task Update_ShouldRecordOnlyChangedProperties_WithOldAndNew()
    {
        await using var db = InMemoryDb.Create(new FakeCurrentUser(_actorId, ActorEmail));
        var workspace = StudioWorkspace.Create("Old name", _actorId);
        db.StudioWorkspaces.Add(workspace);
        await db.SaveChangesAsync();

        workspace.Rename("New name");
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.SingleAsync(a => a.Action == "UPDATE_STUDIOWORKSPACE");
        var changes = JsonDocument.Parse(log.ChangesJson!).RootElement;
        changes.GetProperty("Name").GetProperty("old").GetString().Should().Be("Old name");
        changes.GetProperty("Name").GetProperty("new").GetString().Should().Be("New name");
        changes.TryGetProperty("UpdatedAt", out _).Should().BeFalse("cột audit kỹ thuật không đưa vào ChangesJson");
        workspace.UpdatedBy.Should().Be(_actorId.ToString());
    }

    [Fact]
    public async Task SoftDelete_ShouldBeAuditedAsDelete_AndSetDeletedBy()
    {
        await using var db = InMemoryDb.Create(new FakeCurrentUser(_actorId, ActorEmail));
        var workspaceId = Guid.NewGuid();
        var member = WorkspaceMember.Create(workspaceId, Guid.NewGuid(), WorkspaceRole.Artist);
        db.WorkspaceMembers.Add(member);
        await db.SaveChangesAsync();

        db.WorkspaceMembers.Remove(member);
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.SingleAsync(a => a.Action.StartsWith("DELETE_"));
        log.Action.Should().Be("DELETE_WORKSPACEMEMBER");
        log.WorkspaceId.Should().Be(workspaceId);

        var stored = await db.WorkspaceMembers.IgnoreQueryFilters().SingleAsync(m => m.Id == member.Id);
        stored.IsDeleted.Should().BeTrue();
        stored.DeletedBy.Should().Be(_actorId.ToString());
    }

    [Fact]
    public async Task SensitiveColumns_ShouldBeMasked()
    {
        await using var db = InMemoryDb.Create(new FakeCurrentUser(_actorId, ActorEmail));
        var user = User.Create("u@test.com", "U", passwordHash: "hash-v1");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        user.ResetPassword("hash-v2");
        await db.SaveChangesAsync();

        var logs = await db.AuditLogs.Where(a => a.EntityName == nameof(User)).ToListAsync();
        logs.Should().HaveCount(2);
        logs.Should().OnlyContain(l => !l.ChangesJson!.Contains("hash-v1") && !l.ChangesJson.Contains("hash-v2"));
        logs.Single(l => l.Action == "UPDATE_USER").ChangesJson.Should().Contain("***");
    }

    [Fact]
    public async Task EntityWithSeriesId_ShouldResolveWorkspaceFromSeries()
    {
        await using var db = InMemoryDb.Create(new FakeCurrentUser(_actorId, ActorEmail));
        var workspaceId = Guid.NewGuid();
        var series = DomainSeries.Create(workspaceId, "Series", ReadingDirection.RightToLeft);
        db.Series.Add(series);
        await db.SaveChangesAsync();

        db.TypographyPresets.Add(TypographyPreset.Create(series.Id, "Dialogue", "Anime Ace", 12));
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.SingleAsync(a => a.EntityName == nameof(TypographyPreset));
        log.WorkspaceId.Should().Be(workspaceId);
    }

    [Fact]
    public async Task NoHttpUser_ShouldStillAudit_WithNullActor()
    {
        await using var db = InMemoryDb.Create(currentUser: null);

        db.StudioWorkspaces.Add(StudioWorkspace.Create("Seeded", Guid.NewGuid()));
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.SingleAsync();
        log.UserId.Should().BeNull();
        log.UserEmail.Should().BeNull();
    }

    [Fact]
    public async Task GetAuditLogsQuery_ShouldFilterByUserEntityAndDateRange()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = InMemoryDb.Create(new FakeCurrentUser(_actorId, ActorEmail), dbName);
        db.StudioWorkspaces.Add(StudioWorkspace.Create("Mine", _actorId));
        await db.SaveChangesAsync();

        // Cùng database, request của một user khác
        await using (var otherDbView = InMemoryDb.Create(new FakeCurrentUser(Guid.NewGuid(), "other@test.com"), dbName))
        {
            otherDbView.StudioWorkspaces.Add(StudioWorkspace.Create("Theirs", Guid.NewGuid()));
            await otherDbView.SaveChangesAsync();
        }

        var handler = new GetAuditLogsQueryHandler(db);

        var byUser = await handler.Handle(new GetAuditLogsQuery(UserId: _actorId), CancellationToken.None);
        byUser.Value!.TotalCount.Should().Be(1);

        var byEmail = await handler.Handle(new GetAuditLogsQuery(UserEmail: "OTHER@"), CancellationToken.None);
        byEmail.Value!.TotalCount.Should().Be(1);

        var byEntity = await handler.Handle(new GetAuditLogsQuery(EntityName: nameof(StudioWorkspace)), CancellationToken.None);
        byEntity.Value!.TotalCount.Should().Be(2);

        var future = await handler.Handle(new GetAuditLogsQuery(From: DateTime.UtcNow.AddMinutes(1)), CancellationToken.None);
        future.Value!.TotalCount.Should().Be(0);

        var invalid = await handler.Handle(
            new GetAuditLogsQuery(From: DateTime.UtcNow, To: DateTime.UtcNow.AddDays(-1)), CancellationToken.None);
        invalid.IsSuccess.Should().BeFalse();
    }
}
