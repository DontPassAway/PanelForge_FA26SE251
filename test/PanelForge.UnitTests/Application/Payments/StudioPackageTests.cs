using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PanelForge.API.Controllers;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Commands.SetStudioPackagePrice;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Admin.Queries.GetAdminStudioPackage;
using PanelForge.Application.Features.Payments.Commands.CreateTopup;
using PanelForge.Application.Features.Payments.Commands.PurchaseStudioPackage;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Application.Features.Payments.Queries.GetStudioPackage;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Payments;

/// <summary>
/// D11 (2026-11-28): độc giả mua gói mở Studio một lần bằng Ví → CanCreateStudio vĩnh viễn (BR-22).
/// Admin đặt giá append-only; giá do server quyết định; Admin / Publisher không mua được.
/// </summary>
public class StudioPackageTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly User _admin = User.Create("admin@pf.test", "Ada Admin", role: SystemRole.Admin);
    private readonly User _reader = User.Create("reader@pf.test", "Rin Reader");

    public StudioPackageTests()
    {
        _db.Users.AddRange(_admin, _reader);
        _db.SaveChanges();
    }

    private Task<Result<AdminStudioPackageDto>> SetPrice(int priceVnd) =>
        new SetStudioPackagePriceCommandHandler(_db).Handle(new SetStudioPackagePriceCommand(_admin.Id, priceVnd), default);

    private Task<Result<PurchaseStudioPackageResponse>> Purchase(Guid userId, int expectedPriceVnd) =>
        new PurchaseStudioPackageCommandHandler(_db, new DefaultBankSettings(), new WorkspaceDefaultsSettings())
            .Handle(new PurchaseStudioPackageCommand(userId, expectedPriceVnd), default);

    private void FundWallet(Guid userId, decimal balanceVnd)
    {
        _db.ReaderWallets.Add(ReaderWallet.Create(userId, balanceVnd));
        _db.SaveChanges();
    }

    [Fact]
    public async Task NoPrice_PackageIsNotOnSale_AndPurchaseIsNotFound()
    {
        var package = await new GetStudioPackageQueryHandler(_db).Handle(new GetStudioPackageQuery(), default);
        package.Value.Should().Be(new StudioPackageDto(false, null));

        FundWallet(_reader.Id, 500_000m);
        var result = await Purchase(_reader.Id, 100_000);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        (await _db.Users.SingleAsync(u => u.Id == _reader.Id)).CanCreateStudio.Should().BeFalse();
    }

    [Fact]
    public async Task SetPrice_AppendsHistory_LatestWins_AndWritesAudit()
    {
        (await SetPrice(100_000)).IsSuccess.Should().BeTrue();
        var second = await SetPrice(150_000);

        second.Value!.PriceVnd.Should().Be(150_000);
        second.Value.ChangedByName.Should().Be("Ada Admin");
        _db.StudioPackagePrices.Count().Should().Be(2);
        _db.AuditLogs.Count(a => a.Action == "STUDIO_PACKAGE_PRICE_CHANGED").Should().Be(2);

        var package = await new GetStudioPackageQueryHandler(_db).Handle(new GetStudioPackageQuery(), default);
        package.Value.Should().Be(new StudioPackageDto(true, 150_000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(50_000_001)]
    public async Task SetPrice_OutOfRange_IsValidationError(int priceVnd)
    {
        var result = await SetPrice(priceVnd);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _db.StudioPackagePrices.Should().BeEmpty();
    }

    [Fact]
    public async Task Purchase_DeductsWallet_GrantsStudioCreation_AndRecordsPlatformTransaction()
    {
        await SetPrice(100_000);
        FundWallet(_reader.Id, 120_000m);

        var result = await Purchase(_reader.Id, 100_000);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.RemainingBalanceVnd.Should().Be(20_000);
        result.Value.CanCreateStudio.Should().BeTrue();
        (await _db.Users.SingleAsync(u => u.Id == _reader.Id)).CanCreateStudio.Should().BeTrue();

        var tx = await _db.PaymentTransactions.SingleAsync();
        tx.Type.Should().Be(PaymentTransactionType.StudioPackage);
        tx.Status.Should().Be(PaymentTransactionStatus.Approved);
        tx.AmountVnd.Should().Be(100_000m);
        tx.WorkspaceId.Should().BeNull("doanh thu gói thuộc nền tảng, không tính cho Workspace nào");
        tx.QrImageUrl.Should().BeEmpty("trừ thẳng từ Ví nên hiện trong lịch sử Ví");
        _db.AuditLogs.Count(a => a.Action == "STUDIO_PACKAGE_PURCHASED").Should().Be(1);

        var admin = await new GetAdminStudioPackageQueryHandler(_db).Handle(new GetAdminStudioPackageQuery(), default);
        admin.Value!.PurchaseCount.Should().Be(1);
    }

    [Fact]
    public async Task Purchase_WhenAlreadyAllowed_IsAlreadyOwned_AndDoesNotCharge()
    {
        await SetPrice(100_000);
        FundWallet(_reader.Id, 300_000m);
        (await Purchase(_reader.Id, 100_000)).IsSuccess.Should().BeTrue();

        var again = await Purchase(_reader.Id, 100_000);

        again.ErrorCode.Should().Be(ResultErrorCodes.AlreadyOwned);
        (await _db.ReaderWallets.SingleAsync()).BalanceVnd.Should().Be(200_000m);
    }

    [Fact]
    public async Task Purchase_WithStaleExpectedPrice_IsPriceChanged()
    {
        await SetPrice(100_000);
        FundWallet(_reader.Id, 300_000m);
        await SetPrice(120_000);

        var result = await Purchase(_reader.Id, 100_000);

        result.ErrorCode.Should().Be(ResultErrorCodes.PriceChanged);
        (await _db.ReaderWallets.SingleAsync()).BalanceVnd.Should().Be(300_000m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(50_000)]
    public async Task Purchase_WithInsufficientBalance_IsValidation_AndDoesNotGrant(int? balanceVnd)
    {
        await SetPrice(100_000);
        if (balanceVnd.HasValue) FundWallet(_reader.Id, balanceVnd.Value);

        var result = await Purchase(_reader.Id, 100_000);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await _db.Users.SingleAsync(u => u.Id == _reader.Id)).CanCreateStudio.Should().BeFalse();
        _db.PaymentTransactions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SystemRole.Admin)]
    [InlineData(SystemRole.Publisher)]
    public async Task Purchase_ByAdminOrPublisher_IsForbidden(SystemRole role)
    {
        var staff = User.Create($"{role}@pf.test", $"{role} Staff", role: role);
        _db.Users.Add(staff);
        await SetPrice(100_000);
        FundWallet(staff.Id, 500_000m);

        var result = await Purchase(staff.Id, 100_000);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task StudioPackage_CannotBeOrderedThroughVietQrTopup()
    {
        await SetPrice(100_000);

        var result = await new CreateTopupCommandHandler(_db, new DefaultBankSettings())
            .Handle(new CreateTopupCommand(_reader.Id, new TopupRequest(AmountVnd: 100_000, Purpose: "StudioPackage")), default);

        result.IsSuccess.Should().BeFalse();
        _db.PaymentTransactions.Should().BeEmpty();
    }

    [Fact]
    public void Endpoints_AdminPriceIsAdminOnly_PackageIsPublic_PurchaseNeedsLogin()
    {
        typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Admin");
        typeof(AdminController).GetMethod(nameof(AdminController.SetStudioPackagePrice)).Should().NotBeNull();
        typeof(CatalogController).GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        typeof(CatalogController).GetMethod(nameof(CatalogController.GetStudioPackage)).Should().NotBeNull();
        typeof(WalletController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        typeof(WalletController).GetMethod(nameof(WalletController.PurchaseStudioPackage)).Should().NotBeNull();
    }
}
