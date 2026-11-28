using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Resources.Commands.ReviewResourceRequest;
using PanelForge.Application.Features.Resources.Commands.SubmitResourceRequest;
using PanelForge.Application.Features.Resources.Commands.UpdateResourcePolicy;
using PanelForge.Application.Features.Resources.Commands.UpdateStudioResources;
using PanelForge.Application.Features.Resources.Models;
using PanelForge.Application.Features.Resources.Queries.GetResourceOverview;
using PanelForge.Application.Features.Resources.Queries.GetResourcePolicy;
using PanelForge.Application.Features.Workspaces.Commands.CreateWorkspace;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Resources;

public class ResourceManagementTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly Guid AdminId = Guid.NewGuid();

    private static async Task<(PanelForgeDbContext Db, StudioWorkspace Ws, User Producer)> SeedAsync(long storageQuota = 10 * Gb)
    {
        var db = InMemoryDb.Create(new FakeCurrentUser(AdminId, "admin@test.com"));
        var producer = User.Create("producer@test.com", "Producer");
        db.Users.Add(producer);
        var ws = StudioWorkspace.Create("Studio A", producer.Id, storageQuota);
        db.StudioWorkspaces.Add(ws);
        db.WorkspaceAiConfigs.Add(WorkspaceAiConfig.Create(ws.Id, monthlyTokenQuota: 1000));
        await db.SaveChangesAsync();
        return (db, ws, producer);
    }

    private static User StudioCreator(PanelForgeDbContext db)
    {
        var user = User.Create("creator@test.com", "Creator");
        user.GrantStudioCreation();
        db.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task GetPolicy_WhenNothingSaved_ReturnsDevelopmentDefaults()
    {
        await using var db = InMemoryDb.Create();
        var result = await new GetResourcePolicyQueryHandler(db).Handle(new GetResourcePolicyQuery(), default);

        result.Value!.IsConfigured.Should().BeFalse();
        result.Value.DefaultStorageQuotaBytes.Should().Be(20 * Gb);
        result.Value.DefaultMonthlyTokenQuota.Should().Be(500_000);
        result.Value.WarningThresholdPercent.Should().Be(80);
    }

    [Fact]
    public async Task CreateWorkspace_UsesDefaultPolicy_NotClientQuota()
    {
        await using var db = InMemoryDb.Create();
        var user = StudioCreator(db);
        await db.SaveChangesAsync();

        var result = await new CreateWorkspaceCommandHandler(db).Handle(new CreateWorkspaceCommand(user.Id, "New"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.StorageQuotaBytes.Should().Be(20 * Gb);
        (await db.WorkspaceAiConfigs.SingleAsync()).MonthlyTokenQuota.Should().Be(500_000);
    }

    [Fact]
    public async Task UpdatePolicy_AppliesToNewStudios_AndLeavesExistingQuotasUntouched()
    {
        var (db, existing, _) = await SeedAsync(storageQuota: 10 * Gb);
        await using var _db = db;
        var user = StudioCreator(db);
        await db.SaveChangesAsync();

        var saved = await new UpdateResourcePolicyCommandHandler(db).Handle(
            new UpdateResourcePolicyCommand(new UpdateResourcePolicyRequest(50 * Gb, 2_000_000, 100, false, 90)), default);
        saved.IsSuccess.Should().BeTrue();

        var created = await new CreateWorkspaceCommandHandler(db).Handle(new CreateWorkspaceCommand(user.Id, "After"), default);

        created.Value!.StorageQuotaBytes.Should().Be(50 * Gb);
        var ai = await db.WorkspaceAiConfigs.SingleAsync(c => c.WorkspaceId == created.Value.Id);
        ai.MonthlyTokenQuota.Should().Be(2_000_000);
        ai.MonthlyImageQuota.Should().Be(100);
        ai.IsEnabled.Should().BeFalse();
        (await db.StudioWorkspaces.FindAsync(existing.Id))!.StorageQuotaBytes.Should().Be(10 * Gb);
        db.AuditLogs.Should().Contain(a => a.Action == "CREATE_RESOURCEPOLICY");
    }

    [Theory]
    [InlineData(-1, 0, 0, 80)]
    [InlineData(0, -1, 0, 80)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 0, 100)]
    public async Task UpdatePolicy_InvalidValues_Fails(long storage, long tokens, long images, int threshold)
    {
        await using var db = InMemoryDb.Create();
        var result = await new UpdateResourcePolicyCommandHandler(db).Handle(
            new UpdateResourcePolicyCommand(new UpdateResourcePolicyRequest(storage, tokens, images, true, threshold)), default);

        result.IsSuccess.Should().BeFalse();
        db.ResourcePolicies.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateStudioResources_UpdatesBothQuotas_AndAuditsReason()
    {
        var (db, ws, _) = await SeedAsync();
        await using var _db = db;

        var result = await new UpdateStudioResourcesCommandHandler(db).Handle(new UpdateStudioResourcesCommand(
            ws.Id, AdminId, new UpdateStudioResourcesRequest(30 * Gb, 5000, 20, true, "Mùa phát hành")), default);

        result.IsSuccess.Should().BeTrue();
        (await db.StudioWorkspaces.FindAsync(ws.Id))!.StorageQuotaBytes.Should().Be(30 * Gb);
        var ai = await db.WorkspaceAiConfigs.SingleAsync();
        ai.MonthlyTokenQuota.Should().Be(5000);
        ai.MonthlyImageQuota.Should().Be(20);
        var audit = db.AuditLogs.Single(a => a.Action == "RESOURCE_ALLOCATION_UPDATED");
        audit.Details.Should().Be("Mùa phát hành");
        audit.UserId.Should().Be(AdminId);
        audit.ChangesJson.Should().Contain("10737418240").And.Contain("32212254720");
    }

    [Fact]
    public async Task UpdateStudioResources_RequiresReason_AndRejectsNegative()
    {
        var (db, ws, _) = await SeedAsync();
        await using var _db = db;
        var handler = new UpdateStudioResourcesCommandHandler(db);

        (await handler.Handle(new(ws.Id, AdminId, new(1, 1, 1, true, " ")), default)).IsSuccess.Should().BeFalse();
        (await handler.Handle(new(ws.Id, AdminId, new(-1, 1, 1, true, "x")), default)).IsSuccess.Should().BeFalse();
        (await db.StudioWorkspaces.FindAsync(ws.Id))!.StorageQuotaBytes.Should().Be(10 * Gb);
    }

    [Fact]
    public async Task UpdateStudioResources_LoweringBelowUsage_KeepsUsedStorage()
    {
        var (db, ws, _) = await SeedAsync();
        await using var _db = db;
        ws.TryReserveStorage(5 * Gb).Should().BeTrue();
        await db.SaveChangesAsync();

        await new UpdateStudioResourcesCommandHandler(db).Handle(
            new(ws.Id, AdminId, new(1 * Gb, 1000, 0, true, "Giảm")), default);

        var reloaded = (await db.StudioWorkspaces.FindAsync(ws.Id))!;
        reloaded.UsedStorageBytes.Should().Be(5 * Gb);
        reloaded.HasStorageCapacity(1).Should().BeFalse(); // upload mới bị chặn, file cũ giữ nguyên
    }

    [Fact]
    public async Task SubmitRequest_ValidatesAndBlocksDuplicatePending()
    {
        var (db, ws, producer) = await SeedAsync();
        await using var _db = db;
        var handler = new SubmitResourceRequestCommandHandler(db);

        (await handler.Handle(new(ws.Id, producer.Id, new(ResourceType.Storage, 5 * Gb, "nhỏ hơn")), default))
            .IsSuccess.Should().BeFalse();
        (await handler.Handle(new(ws.Id, producer.Id, new(ResourceType.Storage, 20 * Gb, "")), default))
            .IsSuccess.Should().BeFalse();

        var ok = await handler.Handle(new(ws.Id, producer.Id, new(ResourceType.Storage, 20 * Gb, "Cần thêm chỗ cho chương 5")), default);
        ok.IsSuccess.Should().BeTrue();

        var dup = await handler.Handle(new(ws.Id, producer.Id, new(ResourceType.Storage, 30 * Gb, "lại")), default);
        dup.ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        var request = await db.ResourceRequests.SingleAsync();
        request.CurrentLimit.Should().Be(10 * Gb);
        request.Status.Should().Be(ResourceRequestStatus.Pending);
    }

    [Fact]
    public async Task ApproveRequest_AppliesQuota_AndSecondApprovalConflicts()
    {
        var (db, ws, producer) = await SeedAsync();
        await using var _db = db;
        var submitted = await new SubmitResourceRequestCommandHandler(db).Handle(
            new(ws.Id, producer.Id, new(ResourceType.AiTokens, 5000, "Chạy copilot nhiều")), default);
        var review = new ReviewResourceRequestCommandHandler(db);

        var first = await review.Handle(new(submitted.Value, AdminId, true, null), default);
        var second = await review.Handle(new(submitted.Value, AdminId, true, null), default);

        first.IsSuccess.Should().BeTrue();
        second.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await db.WorkspaceAiConfigs.SingleAsync()).MonthlyTokenQuota.Should().Be(5000);
        var request = await db.ResourceRequests.SingleAsync();
        request.Status.Should().Be(ResourceRequestStatus.Approved);
        request.ReviewedBy.Should().Be(AdminId);
        db.AuditLogs.Should().ContainSingle(a => a.Action == "RESOURCE_REQUEST_APPROVED");
    }

    [Fact]
    public async Task RejectRequest_RequiresNote_AndDoesNotChangeQuota()
    {
        var (db, ws, producer) = await SeedAsync();
        await using var _db = db;
        var submitted = await new SubmitResourceRequestCommandHandler(db).Handle(
            new(ws.Id, producer.Id, new(ResourceType.Storage, 50 * Gb, "Cần thêm")), default);
        var review = new ReviewResourceRequestCommandHandler(db);

        (await review.Handle(new(submitted.Value, AdminId, false, "  "), default)).IsSuccess.Should().BeFalse();
        (await review.Handle(new(submitted.Value, AdminId, false, "Chưa cần thiết"), default)).IsSuccess.Should().BeTrue();

        (await db.StudioWorkspaces.FindAsync(ws.Id))!.StorageQuotaBytes.Should().Be(10 * Gb);
        var request = await db.ResourceRequests.SingleAsync();
        request.Status.Should().Be(ResourceRequestStatus.Rejected);
        request.ReviewNote.Should().Be("Chưa cần thiết");
    }

    [Fact]
    public async Task Overview_FlagsNearLimitAndExhausted_AndCountsPending()
    {
        var (db, ws, producer) = await SeedAsync(storageQuota: 10 * Gb);
        await using var _db = db;
        ws.TryReserveStorage(9 * Gb); // 90% ≥ ngưỡng 80%
        var full = StudioWorkspace.Create("Studio B", producer.Id, 0);
        db.StudioWorkspaces.Add(full);
        var fullAi = WorkspaceAiConfig.Create(full.Id, monthlyTokenQuota: 100);
        fullAi.RecordUsage(100);
        db.WorkspaceAiConfigs.Add(fullAi);
        db.ResourceRequests.Add(ResourceRequest.Create(ws.Id, ResourceType.Storage, 10 * Gb, 20 * Gb, "x", producer.Id));
        await db.SaveChangesAsync();

        var result = (await new GetResourceOverviewQueryHandler(db).Handle(new GetResourceOverviewQuery(), default)).Value!;

        result.Studios.Single(s => s.WorkspaceId == ws.Id).Health.Should().Be(ResourceHealth.NearLimit);
        result.Studios.Single(s => s.WorkspaceId == ws.Id).PendingRequests.Should().Be(1);
        var b = result.Studios.Single(s => s.WorkspaceId == full.Id);
        b.Health.Should().Be(ResourceHealth.Exhausted);
        b.Storage.IsUnlimited.Should().BeTrue();
        result.NearLimitStudios.Should().Be(1);
        result.ExhaustedStudios.Should().Be(1);
        result.PendingRequests.Should().Be(1);
        result.TotalStorageUsedBytes.Should().Be(9 * Gb);
        result.TotalTokensUsedThisMonth.Should().Be(100);
    }

    [Fact]
    public async Task Overview_TreatsPreviousMonthCountersAsZero_ButStorageKeepsUsage()
    {
        var (db, ws, _) = await SeedAsync();
        await using var _db = db;
        ws.TryReserveStorage(1 * Gb);
        var ai = await db.WorkspaceAiConfigs.SingleAsync();
        ai.RecordUsage(900);
        typeof(WorkspaceAiConfig).GetProperty("LastResetAt")!.SetValue(ai, DateTime.UtcNow.AddMonths(-1));
        await db.SaveChangesAsync();

        var studio = (await new GetResourceOverviewQueryHandler(db).Handle(new GetResourceOverviewQuery(), default)).Value!.Studios.Single();

        studio.AiTokens!.Used.Should().Be(0);
        studio.Storage.Used.Should().Be(1 * Gb);
        studio.AiNextResetAt.Day.Should().Be(1);
    }
}
