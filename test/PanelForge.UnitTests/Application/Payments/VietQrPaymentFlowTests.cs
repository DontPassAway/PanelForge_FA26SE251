using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Payments.Commands.ApprovePayment;
using PanelForge.Application.Features.Payments.Commands.CreateTopup;
using PanelForge.Application.Features.Payments.Commands.SubscribeVip;
using PanelForge.Application.Features.Payments.Commands.UnlockChapter;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Payments;

/// <summary>
/// Luồng VietQR Mobile ↔ Producer: đơn Pending mang đúng ngữ cảnh Studio, giá do server quyết định,
/// Producer duyệt thì quyền lợi (ví / chương / VIP) được kích hoạt.
/// </summary>
public class VietQrPaymentFlowTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _readerId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly Chapter _paidChapter;
    private readonly Chapter _freeChapter;

    public VietQrPaymentFlowTests()
    {
        _studio = StudioWorkspace.Create("Studio Moon", _producerId);
        _db.StudioWorkspaces.Add(_studio);

        _series = DomainSeries.Create(_studio.Id, "Moon Blade", ReadingDirection.RightToLeft, "Synopsis", "Fantasy");
        _db.Series.Add(_series);

        _paidChapter = Chapter.Create(_series.Id, 2, "Trả phí");
        _paidChapter.SetPricing(true, 15000m);
        _freeChapter = Chapter.Create(_series.Id, 1, "Miễn phí");
        _db.Chapters.AddRange(_paidChapter, _freeChapter);
        _db.SaveChanges();
    }

    private Task<Result<TopupResponse>> CreateOrder(TopupRequest request) =>
        new CreateTopupCommandHandler(_db).Handle(new CreateTopupCommand(_readerId, request), default);

    [Fact]
    public async Task ChapterUnlockOrder_UsesServerPrice_AndStudioContext_IgnoringClientAmount()
    {
        var result = await CreateOrder(new TopupRequest(AmountVnd: 1000, ChapterId: _paidChapter.Id, Purpose: "ChapterUnlock"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.AmountVnd.Should().Be(15000);
        result.Value.Purpose.Should().Be("ChapterUnlock");
        result.Value.TransferContent.Should().Be(result.Value.OrderId, "nội dung CK phải trùng addInfo trong ảnh QR");
        result.Value.QrCodeUrl.Should().Contain($"addInfo={result.Value.OrderId}").And.Contain("amount=15000");

        var tx = await _db.PaymentTransactions.SingleAsync();
        tx.Status.Should().Be(PaymentTransactionStatus.Pending);
        tx.Type.Should().Be(PaymentTransactionType.ChapterUnlock);
        tx.WorkspaceId.Should().Be(_studio.Id);
        tx.SeriesId.Should().Be(_series.Id);
        tx.ChapterId.Should().Be(_paidChapter.Id);
    }

    [Fact]
    public async Task ChapterUnlockOrder_ForFreeChapter_IsRejected()
    {
        var result = await CreateOrder(new TopupRequest(ChapterId: _freeChapter.Id, Purpose: "ChapterUnlock"));

        result.IsSuccess.Should().BeFalse();
        (await _db.PaymentTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TopupOrder_WithSeriesOnly_IsAttributedToSeriesStudio()
    {
        var result = await CreateOrder(new TopupRequest(AmountVnd: 50000, SeriesId: _series.Id));

        result.IsSuccess.Should().BeTrue();
        (await _db.PaymentTransactions.SingleAsync()).WorkspaceId.Should().Be(_studio.Id);
    }

    [Fact]
    public async Task TopupOrder_WithoutContext_FallsBackToDefaultStudio_PreferringKomaForge()
    {
        var koma = StudioWorkspace.Create("KomaForge Studio", Guid.NewGuid());
        koma.UpdateBankInfo("970436", "Vietcombank", "999888777", "KOMAFORGE");
        _db.StudioWorkspaces.Add(koma);
        await _db.SaveChangesAsync();

        var result = await CreateOrder(new TopupRequest(AmountVnd: 50000));

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccountNumber.Should().Be("999888777");
        (await _db.PaymentTransactions.SingleAsync()).WorkspaceId.Should().Be(koma.Id);
    }

    [Fact]
    public async Task VipOrder_WithoutContext_IsNeverOrphaned()
    {
        await CreateOrder(new TopupRequest(Purpose: "VipSubscription", VipPlan: "monthly"));

        (await _db.PaymentTransactions.SingleAsync()).WorkspaceId.Should().Be(_studio.Id);
    }

    [Fact]
    public async Task RepeatedOrder_WithinWindow_ReusesSamePendingOrder()
    {
        var first = await CreateOrder(new TopupRequest(ChapterId: _paidChapter.Id, Purpose: "ChapterUnlock"));
        var second = await CreateOrder(new TopupRequest(ChapterId: _paidChapter.Id, Purpose: "ChapterUnlock"));

        second.Value!.OrderId.Should().Be(first.Value!.OrderId);
        (await _db.PaymentTransactions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task NewOrder_WithDifferentAmount_ExpiresOlderPendingOfSameType()
    {
        var first = await CreateOrder(new TopupRequest(AmountVnd: 20000, SeriesId: _series.Id));
        var second = await CreateOrder(new TopupRequest(AmountVnd: 50000, SeriesId: _series.Id));

        second.Value!.OrderId.Should().NotBe(first.Value!.OrderId);
        var old = await _db.PaymentTransactions.SingleAsync(t => t.OrderCode == first.Value.OrderId);
        old.Status.Should().Be(PaymentTransactionStatus.Expired);
        (await _db.PaymentTransactions.CountAsync(t => t.Status == PaymentTransactionStatus.Pending)).Should().Be(1);
    }

    [Fact]
    public async Task TopupOrder_BelowMinimum_IsRejected()
    {
        (await CreateOrder(new TopupRequest(AmountVnd: 500))).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task UnknownPurpose_IsRejected()
    {
        (await CreateOrder(new TopupRequest(AmountVnd: 50000, Purpose: "Refund"))).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ApprovingChapterUnlockOrder_GrantsEntitlement()
    {
        var order = await CreateOrder(new TopupRequest(ChapterId: _paidChapter.Id, Purpose: "ChapterUnlock"));
        var tx = await _db.PaymentTransactions.SingleAsync(t => t.OrderCode == order.Value!.OrderId);

        var approved = await new ApprovePaymentCommandHandler(_db)
            .Handle(new ApprovePaymentCommand(_studio.Id, tx.Id, _producerId), default);

        approved.IsSuccess.Should().BeTrue();
        (await _db.ChapterEntitlements.AnyAsync(e => e.UserId == _readerId && e.ChapterId == _paidChapter.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ApprovingYearlyVipOrder_GrantsOneYear()
    {
        var order = await CreateOrder(new TopupRequest(Purpose: "VipSubscription", VipPlan: "yearly", SeriesId: _series.Id));
        order.Value!.AmountVnd.Should().Be(VipPricing.YearlyVnd);
        var tx = await _db.PaymentTransactions.SingleAsync();

        await new ApprovePaymentCommandHandler(_db).Handle(new ApprovePaymentCommand(_studio.Id, tx.Id, _producerId), default);

        var wallet = await _db.ReaderWallets.SingleAsync(w => w.UserId == _readerId);
        wallet.IsVip.Should().BeTrue();
        wallet.VipExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(365), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task WalletUnlock_ChargesChapterPrice_AndRecordsTransaction()
    {
        _db.ReaderWallets.Add(ReaderWallet.Create(_readerId, 20000m));
        await _db.SaveChangesAsync();

        var result = await new UnlockChapterCommandHandler(_db)
            .Handle(new UnlockChapterCommand(_readerId, new UnlockChapterRequest(_paidChapter.Id, PriceVnd: 1)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RemainingBalanceVnd.Should().Be(5000, "giá lấy từ chương, không phải từ client");
        var tx = await _db.PaymentTransactions.SingleAsync();
        tx.Status.Should().Be(PaymentTransactionStatus.Approved);
        tx.QrImageUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task WalletVip_DeductsMonthlyPrice()
    {
        _db.ReaderWallets.Add(ReaderWallet.Create(_readerId, 50000m));
        await _db.SaveChangesAsync();

        var result = await new SubscribeVipCommandHandler(_db)
            .Handle(new SubscribeVipCommand(_readerId, new SubscribeVipRequest("monthly")), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsVip.Should().BeTrue();
        (await _db.ReaderWallets.SingleAsync()).BalanceVnd.Should().Be(50000m - VipPricing.MonthlyVnd);
    }
}
