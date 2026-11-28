using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Quotas.Commands.ApproveQuotaIncreaseRequest;
using PanelForge.Application.Features.Quotas.Commands.RejectQuotaIncreaseRequest;
using PanelForge.Application.Features.Quotas.Commands.SubmitQuotaIncreaseRequest;
using PanelForge.Application.Features.Quotas.Commands.UpdateQuotaPolicy;
using PanelForge.Application.Features.Quotas.Commands.UpdateWorkspaceResources;
using PanelForge.Application.Features.Quotas.Queries.GetAdminResources;
using PanelForge.Application.Features.Quotas.Queries.GetQuotaPolicy;
using PanelForge.Application.Features.Workspaces.Commands.CreateWorkspace;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Quotas;

/// <summary>OPS-20 chính sách hạn mức, OPS-21 xin tăng hạn mức, OPS-23 màn Tài nguyên (BR-22, BR-18).</summary>
public class QuotaGovernanceTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static async Task<(PanelForgeDbContext Db, User Admin, User Producer, StudioWorkspace Ws, WorkspaceAiConfig Ai)> SeedAsync()
    {
        var db = InMemoryDb.Create();
        var admin = User.Create("admin@test.com", "Admin", role: SystemRole.Admin);
        var producer = User.Create("p@test.com", "Producer");
        var ws = StudioWorkspace.Create("Studio A", producer.Id, 5 * Gb);
        var ai = WorkspaceAiConfig.Create(ws.Id, monthlyTokenQuota: 1_000_000);
        ai.SetMonthlyImageQuota(100);
        db.Users.AddRange(admin, producer);
        db.StudioWorkspaces.Add(ws);
        db.WorkspaceAiConfigs.Add(ai);
        await db.SaveChangesAsync();
        return (db, admin, producer, ws, ai);
    }

    [Fact]
    public async Task GetQuotaPolicy_WithoutSavedPolicy_FallsBackToSettings_OPS20()
    {
        await using var db = InMemoryDb.Create();
        var settings = new WorkspaceDefaultsSettings { StorageQuotaBytes = 3 * Gb, MonthlyTokenQuota = 500, MonthlyImageQuota = 7 };

        var result = await new GetQuotaPolicyQueryHandler(db, settings).Handle(new GetQuotaPolicyQuery(), CancellationToken.None);

        result.Value!.IsFromSettings.Should().BeTrue();
        result.Value.StorageQuotaBytes.Should().Be(3 * Gb);
        result.Value.MonthlyImageQuota.Should().Be(7);
    }

    [Fact]
    public async Task UpdateQuotaPolicy_AppendsHistory_AndNewStudioUsesLatest_OPS20()
    {
        var (db, admin, _, existing, _) = await SeedAsync();
        await using var _db = db;
        var settings = new WorkspaceDefaultsSettings();
        var update = new UpdateQuotaPolicyCommandHandler(db, settings);

        await update.Handle(new UpdateQuotaPolicyCommand(admin.Id, 8 * Gb, 2_000_000, 50), CancellationToken.None);
        var latest = await update.Handle(new UpdateQuotaPolicyCommand(admin.Id, 10 * Gb, 3_000_000, 60), CancellationToken.None);

        latest.Value!.IsFromSettings.Should().BeFalse();
        latest.Value.UpdatedByName.Should().Be("Admin");
        (await db.QuotaPolicies.CountAsync()).Should().Be(2, "chính sách là append-only");

        var creator = User.Create("c@test.com", "Creator");
        creator.GrantStudioCreation();
        db.Users.Add(creator);
        await db.SaveChangesAsync();
        var created = await new CreateWorkspaceCommandHandler(db, settings)
            .Handle(new CreateWorkspaceCommand(creator.Id, "Studio mới"), CancellationToken.None);

        created.Value!.StorageQuotaBytes.Should().Be(10 * Gb);
        var ai = await db.WorkspaceAiConfigs.SingleAsync(c => c.WorkspaceId == created.Value.Id);
        ai.MonthlyTokenQuota.Should().Be(3_000_000);
        ai.MonthlyImageQuota.Should().Be(60);

        (await db.StudioWorkspaces.FindAsync(existing.Id))!.StorageQuotaBytes.Should().Be(5 * Gb, "Studio đã có giữ mức cấp riêng");
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    public async Task UpdateQuotaPolicy_RejectsZeroOrUnlimited_OPS20(long storage, long tokens, long images)
    {
        await using var db = InMemoryDb.Create();
        var result = await new UpdateQuotaPolicyCommandHandler(db, new WorkspaceDefaultsSettings())
            .Handle(new UpdateQuotaPolicyCommand(Guid.NewGuid(), storage, tokens, images), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await db.QuotaPolicies.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Submit_CreatesPendingRequest_AndNotifiesAdmins_OPS21()
    {
        var (db, admin, producer, ws, _) = await SeedAsync();
        await using var _db = db;

        var result = await new SubmitQuotaIncreaseRequestCommandHandler(db).Handle(
            new SubmitQuotaIncreaseRequestCommand(ws.Id, producer.Id, QuotaResourceType.Storage, 20 * Gb, "  Thêm 3 bộ mới  "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(QuotaIncreaseRequestStatus.Pending);
        result.Value.CurrentQuota.Should().Be(5 * Gb);
        result.Value.Reason.Should().Be("Thêm 3 bộ mới");
        result.Value.RequestedByName.Should().Be("Producer");
        db.Notifications.Should().ContainSingle(n => n.UserId == admin.Id && n.Type == NotificationType.QuotaIncreaseRequested);
    }

    [Fact]
    public async Task Submit_SecondPendingOfSameType_IsConflict_OPS21()
    {
        var (db, _, producer, ws, _) = await SeedAsync();
        await using var _db = db;
        var handler = new SubmitQuotaIncreaseRequestCommandHandler(db);
        await handler.Handle(new SubmitQuotaIncreaseRequestCommand(ws.Id, producer.Id, QuotaResourceType.AiTokens, 2_000_000, "Cần thêm"), CancellationToken.None);

        var second = await handler.Handle(
            new SubmitQuotaIncreaseRequestCommand(ws.Id, producer.Id, QuotaResourceType.AiTokens, 3_000_000, "Cần thêm nữa"), CancellationToken.None);
        var otherType = await handler.Handle(
            new SubmitQuotaIncreaseRequestCommand(ws.Id, producer.Id, QuotaResourceType.AiImages, 200, "Ảnh bìa"), CancellationToken.None);

        second.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        otherType.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(5L * 1024 * 1024 * 1024, "Lý do")] // không lớn hơn mức đang cấp
    [InlineData(50L * 1024 * 1024 * 1024, "   ")] // thiếu lý do
    public async Task Submit_InvalidInput_IsValidationError_OPS21(long requested, string reason)
    {
        var (db, _, producer, ws, _) = await SeedAsync();
        await using var _db = db;

        var result = await new SubmitQuotaIncreaseRequestCommandHandler(db).Handle(
            new SubmitQuotaIncreaseRequestCommand(ws.Id, producer.Id, QuotaResourceType.Storage, requested, reason), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await db.QuotaIncreaseRequests.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Submit_WhenQuotaIsUnlimited_IsValidationError_OPS21()
    {
        var (db, _, producer, ws, _) = await SeedAsync();
        await using var _db = db;
        ws.SetStorageQuota(0);
        await db.SaveChangesAsync();

        var result = await new SubmitQuotaIncreaseRequestCommandHandler(db).Handle(
            new SubmitQuotaIncreaseRequestCommand(ws.Id, producer.Id, QuotaResourceType.Storage, 10 * Gb, "Lý do"), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Approve_AppliesGrantedQuota_AndNotifiesProducer_OPS21()
    {
        var (db, admin, producer, ws, ai) = await SeedAsync();
        await using var _db = db;
        var submitted = await new SubmitQuotaIncreaseRequestCommandHandler(db).Handle(
            new SubmitQuotaIncreaseRequestCommand(ws.Id, producer.Id, QuotaResourceType.AiTokens, 5_000_000, "Chạy AI cho 10 chương"), CancellationToken.None);

        var approved = await new ApproveQuotaIncreaseRequestCommandHandler(db).Handle(
            new ApproveQuotaIncreaseRequestCommand(submitted.Value!.Id, admin.Id, 3_000_000, "Cấp 3 triệu trước"), CancellationToken.None);

        approved.Value!.Status.Should().Be(QuotaIncreaseRequestStatus.Approved);
        approved.Value.GrantedQuota.Should().Be(3_000_000);
        approved.Value.ReviewedByName.Should().Be("Admin");
        (await db.WorkspaceAiConfigs.FindAsync(ai.Id))!.MonthlyTokenQuota.Should().Be(3_000_000);
        db.Notifications.Should().ContainSingle(n => n.UserId == producer.Id && n.Type == NotificationType.QuotaIncreaseApproved);
        db.AuditLogs.Should().Contain(l => l.EntityName == nameof(QuotaIncreaseRequest) && l.Action == "UPDATE_QUOTAINCREASEREQUEST");
    }

    [Fact]
    public async Task Approve_AlreadyReviewed_IsConflict_OPS21()
    {
        var (db, admin, producer, ws, _) = await SeedAsync();
        await using var _db = db;
        var submitted = await new SubmitQuotaIncreaseRequestCommandHandler(db).Handle(
            new SubmitQuotaIncreaseRequestCommand(ws.Id, producer.Id, QuotaResourceType.Storage, 10 * Gb, "Lý do"), CancellationToken.None);
        await new RejectQuotaIncreaseRequestCommandHandler(db).Handle(
            new RejectQuotaIncreaseRequestCommand(submitted.Value!.Id, admin.Id, "Chưa cần"), CancellationToken.None);

        var approved = await new ApproveQuotaIncreaseRequestCommandHandler(db).Handle(
            new ApproveQuotaIncreaseRequestCommand(submitted.Value.Id, admin.Id, null, null), CancellationToken.None);

        approved.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await db.StudioWorkspaces.FindAsync(ws.Id))!.StorageQuotaBytes.Should().Be(5 * Gb);
    }

    [Fact]
    public async Task Reject_RequiresNote_AndKeepsQuota_OPS21()
    {
        var (db, admin, producer, ws, _) = await SeedAsync();
        await using var _db = db;
        var submitted = await new SubmitQuotaIncreaseRequestCommandHandler(db).Handle(
            new SubmitQuotaIncreaseRequestCommand(ws.Id, producer.Id, QuotaResourceType.Storage, 10 * Gb, "Lý do"), CancellationToken.None);
        var reject = new RejectQuotaIncreaseRequestCommandHandler(db);

        var noNote = await reject.Handle(new RejectQuotaIncreaseRequestCommand(submitted.Value!.Id, admin.Id, " "), CancellationToken.None);
        var ok = await reject.Handle(new RejectQuotaIncreaseRequestCommand(submitted.Value.Id, admin.Id, "Dọn file cũ trước"), CancellationToken.None);

        noNote.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        ok.Value!.Status.Should().Be(QuotaIncreaseRequestStatus.Rejected);
        ok.Value.ReviewNote.Should().Be("Dọn file cũ trước");
        (await db.StudioWorkspaces.FindAsync(ws.Id))!.StorageQuotaBytes.Should().Be(5 * Gb);
        db.Notifications.Should().ContainSingle(n => n.UserId == producer.Id && n.Type == NotificationType.QuotaIncreaseRejected);
    }

    [Fact]
    public async Task AdminResources_ListsUsageAndPendingCount_OPS23()
    {
        var (db, _, producer, ws, _) = await SeedAsync();
        await using var _db = db;
        await new SubmitQuotaIncreaseRequestCommandHandler(db).Handle(
            new SubmitQuotaIncreaseRequestCommand(ws.Id, producer.Id, QuotaResourceType.Storage, 10 * Gb, "Lý do"), CancellationToken.None);

        var rows = (await new GetAdminResourcesQueryHandler(db).Handle(new GetAdminResourcesQuery(), CancellationToken.None)).Value!;

        var row = rows.Should().ContainSingle().Subject;
        row.StorageQuotaBytes.Should().Be(5 * Gb);
        row.MonthlyImageQuota.Should().Be(100);
        row.HasAiConfig.Should().BeTrue();
        row.PendingRequestCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateWorkspaceResources_NullKeeps_ZeroIsUnlimited_OPS23()
    {
        var (db, _, _, ws, ai) = await SeedAsync();
        await using var _db = db;

        var result = await new UpdateWorkspaceResourcesCommandHandler(db).Handle(
            new UpdateWorkspaceResourcesCommand(ws.Id, 0, null, 250), CancellationToken.None);

        result.Value!.StorageQuotaBytes.Should().Be(0);
        result.Value.MonthlyTokenQuota.Should().Be(1_000_000);
        result.Value.MonthlyImageQuota.Should().Be(250);
    }

    [Fact]
    public async Task UpdateWorkspaceResources_CreatesAiConfigWhenMissing_OPS23()
    {
        await using var db = InMemoryDb.Create();
        var owner = User.Create("o@test.com", "Owner");
        var ws = StudioWorkspace.Create("No AI", owner.Id, Gb);
        db.Users.Add(owner);
        db.StudioWorkspaces.Add(ws);
        await db.SaveChangesAsync();

        var result = await new UpdateWorkspaceResourcesCommandHandler(db).Handle(
            new UpdateWorkspaceResourcesCommand(ws.Id, null, 2_000, null), CancellationToken.None);

        result.Value!.HasAiConfig.Should().BeTrue();
        result.Value.MonthlyTokenQuota.Should().Be(2_000);
    }
}
