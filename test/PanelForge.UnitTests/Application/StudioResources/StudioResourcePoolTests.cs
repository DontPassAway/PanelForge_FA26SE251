using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Commands.SetStudioPackagePrice;
using PanelForge.Application.Features.Payments.Commands.PurchaseStudioPackage;
using PanelForge.Application.Features.Admin.Commands.UpdateWorkspaceQuota;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Application.Features.Publisher.Queries.GetPublisherRevenue;
using PanelForge.Application.Features.Quotas.Commands.ApproveQuotaIncreaseRequest;
using PanelForge.Application.Features.Quotas.Commands.SubmitQuotaIncreaseRequest;
using PanelForge.Application.Features.Quotas.Commands.UpdateWorkspaceResources;
using PanelForge.Application.Features.StudioResources.Commands.AllocateStudioResources;
using PanelForge.Application.Features.StudioResources.Commands.PurchaseStudioResources;
using PanelForge.Application.Features.StudioResources.Commands.SetStudioResourcePrice;
using PanelForge.Application.Features.StudioResources.Models;
using PanelForge.Application.Features.StudioResources.Queries.GetMyStudioResources;
using PanelForge.Application.Features.Workspaces.Commands.CreateWorkspace;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.StudioResources;

/// <summary>
/// D12 (2026-11-28): gói Mở Studio kèm kho tài nguyên cho một Studio; một gói tạo được nhiều Studio; Producer chia kho
/// (dung lượng, token AI mua một lần) cho từng Studio, thấy phần còn lại; thiếu thì báo; dùng hết thì mua thêm bằng Ví.
/// </summary>
public class StudioResourcePoolTests
{
    private const long Gb = 1L << 30;

    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly WorkspaceDefaultsSettings _defaults = new() { StorageQuotaBytes = 5 * Gb, MonthlyTokenQuota = 1_000_000 };
    private readonly User _admin = User.Create("admin@pf.test", "Ada Admin", role: SystemRole.Admin);
    private readonly User _producer = User.Create("producer@pf.test", "Pia Producer");

    public StudioResourcePoolTests()
    {
        _db.Users.AddRange(_admin, _producer);
        _db.SaveChanges();
    }

    private async Task BuyPackage()
    {
        await new SetStudioPackagePriceCommandHandler(_db).Handle(new SetStudioPackagePriceCommand(_admin.Id, 100_000), default);
        _db.ReaderWallets.Add(ReaderWallet.Create(_producer.Id, 1_000_000m));
        await _db.SaveChangesAsync();
        var bought = await new PurchaseStudioPackageCommandHandler(_db, new DefaultBankSettings(), _defaults)
            .Handle(new PurchaseStudioPackageCommand(_producer.Id, 100_000), default);
        bought.IsSuccess.Should().BeTrue(bought.ErrorMessage);
    }

    private Task<Result<WorkspaceDto>> Create(string name, long? storage = null, long? tokens = null) =>
        new CreateWorkspaceCommandHandler(_db, _defaults)
            .Handle(new CreateWorkspaceCommand(_producer.Id, name, storage, tokens), default);

    private async Task<StudioResourcesDto> Resources() =>
        (await new GetMyStudioResourcesQueryHandler(_db, _defaults).Handle(new GetMyStudioResourcesQuery(_producer.Id), default)).Value!;

    private Task<Result<StudioResourcesDto>> Allocate(Guid workspaceId, long storage, long tokens, Guid? by = null) =>
        new AllocateStudioResourcesCommandHandler(_db, _defaults)
            .Handle(new AllocateStudioResourcesCommand(by ?? _producer.Id, workspaceId, storage, tokens), default);

    [Fact]
    public async Task PackagePurchase_GrantsPoolForOneStudio_AtPolicyDefaults()
    {
        await BuyPackage();

        var resources = await Resources();

        resources.HasPool.Should().BeTrue();
        resources.Storage.Should().Be(new ResourceBudgetDto(5 * Gb, 0, 5 * Gb));
        resources.Tokens.Should().Be(new ResourceBudgetDto(1_000_000, 0, 1_000_000));
    }

    [Fact]
    public async Task CreateStudio_TakesShareFromPool_AndSecondStudioFailsWhenPoolIsShort()
    {
        await BuyPackage();

        var first = await Create("Studio A", 3 * Gb, 600_000);
        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        var ai = await _db.WorkspaceAiConfigs.SingleAsync(c => c.WorkspaceId == first.Value!.Id);
        ai.IsPrepaidTokenBudget.Should().BeTrue("token chia từ kho là token mua một lần");
        ai.MonthlyTokenQuota.Should().Be(600_000);

        var resources = await Resources();
        resources.Storage!.Remaining.Should().Be(2 * Gb);
        resources.Tokens!.Remaining.Should().Be(400_000);

        var second = await Create("Studio B", 3 * Gb, 100_000);
        second.ErrorCode.Should().Be(ResultErrorCodes.InsufficientStudioResources);
        _db.StudioWorkspaces.Count().Should().Be(1);

        var fits = await Create("Studio B", 2 * Gb, 400_000);
        fits.IsSuccess.Should().BeTrue("một gói tạo được nhiều Studio khi kho còn đủ");
        (await Resources()).Studios.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateStudio_WithoutPool_KeepsLegacyPolicyQuota()
    {
        _producer.GrantStudioCreation();
        await _db.SaveChangesAsync();

        var created = await Create("Legacy", 1, 1);

        created.Value!.StorageQuotaBytes.Should().Be(5 * Gb, "Producer chưa có kho giữ hạn mức do chính sách Admin đặt");
        (await _db.WorkspaceAiConfigs.SingleAsync()).IsPrepaidTokenBudget.Should().BeFalse();
        (await Resources()).HasPool.Should().BeFalse();
    }

    [Fact]
    public async Task Allocate_RespectsPool_UsedStorage_AndOwnership()
    {
        await BuyPackage();
        var a = (await Create("A", 2 * Gb, 500_000)).Value!;
        var b = (await Create("B", 2 * Gb, 300_000)).Value!;

        (await Allocate(a.Id, 3 * Gb, 700_000)).IsSuccess.Should().BeTrue();
        (await Allocate(a.Id, 3 * Gb + 1, 700_000)).ErrorCode.Should().Be(ResultErrorCodes.InsufficientStudioResources);

        var workspace = await _db.StudioWorkspaces.SingleAsync(w => w.Id == b.Id);
        workspace.UpdateUsedStorage(Gb + 5);
        await _db.SaveChangesAsync();
        (await Allocate(b.Id, Gb, 300_000)).ErrorCode.Should().Be(ResultErrorCodes.Validation, "không chia thấp hơn phần đã dùng");

        var member = User.Create("member@pf.test", "Mai Member");
        _db.Users.Add(member);
        await _db.SaveChangesAsync();
        (await Allocate(a.Id, Gb, 1, member.Id)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        _db.AuditLogs.Count(l => l.Action == "STUDIO_RESOURCES_ALLOCATED").Should().Be(1);
    }

    [Fact]
    public void PrepaidTokens_AreNotResetAtMonthStart()
    {
        var config = WorkspaceAiConfig.Create(Guid.NewGuid(), monthlyTokenQuota: 1_000);
        config.UsePrepaidTokenBudget(1_000);
        config.RecordUsage(900);

        config.ResetMonthlyUsage();

        config.UsedTokensCurrentMonth.Should().Be(900, "token mua một lần dùng hết thì mua thêm, không trả lại đầu tháng");
        config.HasQuotaRemaining().Should().BeTrue();
        config.RecordUsage(100);
        config.HasQuotaRemaining().Should().BeFalse();
    }

    [Fact]
    public async Task BuyMore_FromWallet_AddsToPool_AtServerPrice()
    {
        await BuyPackage();
        var priceResult = await new SetStudioResourcePriceCommandHandler(_db)
            .Handle(new SetStudioResourcePriceCommand(_admin.Id, 20_000, 50_000), default);
        priceResult.IsSuccess.Should().BeTrue();

        var handler = new PurchaseStudioResourcesCommandHandler(_db, new DefaultBankSettings(), _defaults);
        (await handler.Handle(new PurchaseStudioResourcesCommand(_producer.Id, 2, 1, 80_000), default))
            .ErrorCode.Should().Be(ResultErrorCodes.PriceChanged);

        var bought = await handler.Handle(new PurchaseStudioResourcesCommand(_producer.Id, 2, 1, 90_000), default);

        bought.IsSuccess.Should().BeTrue(bought.ErrorMessage);
        bought.Value!.RemainingBalanceVnd.Should().Be(1_000_000 - 100_000 - 90_000);
        bought.Value.Resources.Storage!.Total.Should().Be(7 * Gb);
        bought.Value.Resources.Tokens!.Total.Should().Be(2_000_000);
        (await _db.PaymentTransactions.CountAsync(t => t.Type == PaymentTransactionType.StudioResources)).Should().Be(1);
    }

    [Fact]
    public async Task BuyMore_WithoutStudioPackage_IsForbidden()
    {
        await new SetStudioResourcePriceCommandHandler(_db).Handle(new SetStudioResourcePriceCommand(_admin.Id, 20_000, 50_000), default);

        var result = await new PurchaseStudioResourcesCommandHandler(_db, new DefaultBankSettings(), _defaults)
            .Handle(new PurchaseStudioResourcesCommand(_producer.Id, 1, 0, 20_000), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task BuyMore_ByLegacyProducer_StartsPoolFromCurrentAllocations()
    {
        _producer.GrantStudioCreation();
        _db.ReaderWallets.Add(ReaderWallet.Create(_producer.Id, 1_000_000m));
        await _db.SaveChangesAsync();
        (await Create("Legacy")).IsSuccess.Should().BeTrue();
        await new SetStudioResourcePriceCommandHandler(_db).Handle(new SetStudioResourcePriceCommand(_admin.Id, 20_000, 50_000), default);

        var bought = await new PurchaseStudioResourcesCommandHandler(_db, new DefaultBankSettings(), _defaults)
            .Handle(new PurchaseStudioResourcesCommand(_producer.Id, 10, 2, 300_000), default);

        bought.IsSuccess.Should().BeTrue(bought.ErrorMessage);
        bought.Value!.Resources.Storage.Should().Be(new ResourceBudgetDto(15 * Gb, 5 * Gb, 10 * Gb));
        bought.Value.Resources.Tokens.Should().Be(new ResourceBudgetDto(3_000_000, 1_000_000, 2_000_000));
    }

    // Admin đổi hạn mức Studio của Producer có kho: phải nằm trong kho, không tặng thêm ngoài phần đã mua.
    [Fact]
    public async Task AdminUpdateResources_OnPooledStudio_CannotExceedPoolOrSetUnlimited()
    {
        await BuyPackage();
        var studio = (await Create("A", 2 * Gb, 400_000)).Value!;
        var handler = new UpdateWorkspaceResourcesCommandHandler(_db, _defaults);

        var tooMuch = await handler.Handle(
            new UpdateWorkspaceResourcesCommand(studio.Id, _admin.Id, 6 * Gb, null, null, null, "Tăng"), default);
        var unlimited = await handler.Handle(
            new UpdateWorkspaceResourcesCommand(studio.Id, _admin.Id, null, 0, null, null, "Bỏ giới hạn"), default);
        var withinPool = await handler.Handle(
            new UpdateWorkspaceResourcesCommand(studio.Id, _admin.Id, 5 * Gb, 1_000_000, null, null, "Dùng hết kho"), default);

        tooMuch.ErrorCode.Should().Be(ResultErrorCodes.InsufficientStudioResources);
        unlimited.ErrorCode.Should().Be(ResultErrorCodes.InsufficientStudioResources);
        withinPool.IsSuccess.Should().BeTrue(withinPool.ErrorMessage);
        (await Resources()).Storage.Should().Be(new ResourceBudgetDto(5 * Gb, 5 * Gb, 0));
    }

    [Fact]
    public async Task AdminUpdateQuota_OnPooledStudio_CannotExceedPool()
    {
        await BuyPackage();
        var studio = (await Create("A", 2 * Gb, 400_000)).Value!;

        var result = await new UpdateWorkspaceQuotaCommandHandler(_db).Handle(new UpdateWorkspaceQuotaCommand(studio.Id, 8 * Gb), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.InsufficientStudioResources);
        (await _db.StudioWorkspaces.SingleAsync(w => w.Id == studio.Id)).StorageQuotaBytes.Should().Be(2 * Gb);
    }

    [Fact]
    public async Task ApproveIncrease_OnPooledStudio_BeyondPool_IsRejectedAndRequestStaysPending()
    {
        await BuyPackage();
        var studio = (await Create("A", 2 * Gb, 400_000)).Value!;
        var submitted = await new SubmitQuotaIncreaseRequestCommandHandler(_db).Handle(
            new SubmitQuotaIncreaseRequestCommand(studio.Id, _producer.Id, QuotaResourceType.Storage, 10 * Gb, "Thêm bộ mới"), default);
        submitted.IsSuccess.Should().BeTrue(submitted.ErrorMessage);

        var approved = await new ApproveQuotaIncreaseRequestCommandHandler(_db).Handle(
            new ApproveQuotaIncreaseRequestCommand(submitted.Value!.Id, _admin.Id, 10 * Gb, null), default);

        approved.ErrorCode.Should().Be(ResultErrorCodes.InsufficientStudioResources);
        _db.ChangeTracker.Clear();
        (await _db.QuotaIncreaseRequests.SingleAsync()).Status.Should().Be(QuotaIncreaseRequestStatus.Pending);
        (await _db.StudioWorkspaces.SingleAsync(w => w.Id == studio.Id)).StorageQuotaBytes.Should().Be(2 * Gb);
    }

    [Fact]
    public async Task PublisherRevenue_IncludesStudioPackageAndResourceSales()
    {
        await BuyPackage();
        await new SetStudioResourcePriceCommandHandler(_db).Handle(new SetStudioResourcePriceCommand(_admin.Id, 20_000, 50_000), default);
        (await new PurchaseStudioResourcesCommandHandler(_db, new DefaultBankSettings(), _defaults)
            .Handle(new PurchaseStudioResourcesCommand(_producer.Id, 1, 1, 70_000), default)).IsSuccess.Should().BeTrue();

        var summary = (await new GetPublisherRevenueQueryHandler(_db).Handle(new GetPublisherRevenueQuery(), default)).Value!.Summary;

        summary.StudioPackageRevenueVnd.Should().Be(100_000m);
        summary.StudioResourcesRevenueVnd.Should().Be(70_000m);
        summary.TotalRevenueVnd.Should().Be(170_000m);
    }
}
