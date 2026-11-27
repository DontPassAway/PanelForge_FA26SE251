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
        _freeChapter = Chapter.Create(_series.Id, 1, "Miễn phí");
        foreach (var ch in new[] { _paidChapter, _freeChapter })
        {
            typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(ch, ChapterStatus.HandedOffAndPublished);
            typeof(Chapter).GetProperty(nameof(Chapter.PublishedAt))!.SetValue(ch, DateTime.UtcNow.AddDays(-1));
        }
        _db.Chapters.AddRange(_paidChapter, _freeChapter);
        TestEditions.Monetize(_db, _paidChapter.Id, isPaid: true, priceVnd: 15000m);
        TestEditions.Monetize(_db, _freeChapter.Id, isPaid: false);
        _db.SaveChanges();
    }

    private Task<Result<TopupResponse>> CreateOrder(TopupRequest request) =>
        new CreateTopupCommandHandler(_db, new DefaultBankSettings()).Handle(new CreateTopupCommand(_readerId, request), default);

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

        var result = await new UnlockChapterCommandHandler(_db, new DefaultBankSettings())
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

        var result = await new SubscribeVipCommandHandler(_db, new DefaultBankSettings())
            .Handle(new SubscribeVipCommand(_readerId, new SubscribeVipRequest("monthly")), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsVip.Should().BeTrue();
        (await _db.ReaderWallets.SingleAsync()).BalanceVnd.Should().Be(50000m - VipPricing.MonthlyVnd);
    }

    [Fact]
    public async Task WorkspacePaymentsSummary_SeparatesTopupsFromUnlockRevenue_NoDoubleCounting()
    {
        var reader = PanelForge.Domain.Entities.Identity.User.Create("reader@test.local", "Reader");
        _db.Users.Add(reader);
        await _db.SaveChangesAsync();

        var topupOrder = await new CreateTopupCommandHandler(_db, new DefaultBankSettings())
            .Handle(new CreateTopupCommand(reader.Id, new TopupRequest(AmountVnd: 100000, SeriesId: _series.Id)), default);
        var topupTx = await _db.PaymentTransactions.SingleAsync(t => t.OrderCode == topupOrder.Value!.OrderId);
        await new ApprovePaymentCommandHandler(_db)
            .Handle(new ApprovePaymentCommand(_studio.Id, topupTx.Id, _producerId), default);

        var queryHandler = new PanelForge.Application.Features.Payments.Queries.GetWorkspacePayments.GetWorkspacePaymentsQueryHandler(_db);
        var afterTopup = (await queryHandler.Handle(new(_studio.Id), default)).Value!;

        afterTopup.Summary.TotalTopupsVnd.Should().Be(100000m);
        afterTopup.Summary.TotalUnlockRevenueVnd.Should().Be(0m);
        afterTopup.Summary.TotalRevenueVnd.Should().Be(0m, "nạp ví không phải doanh thu mở khóa (AUD-BE-07)");

        // Độc giả dùng ví mở khóa chương 15.000đ → doanh thu mở khóa tăng 15.000đ, tổng nạp ví giữ nguyên 100.000đ
        var unlock = await new UnlockChapterCommandHandler(_db, new DefaultBankSettings())
            .Handle(new UnlockChapterCommand(reader.Id, new UnlockChapterRequest(_paidChapter.Id)), default);
        unlock.IsSuccess.Should().BeTrue();

        var afterUnlock = (await queryHandler.Handle(new(_studio.Id), default)).Value!;
        afterUnlock.Summary.TotalTopupsVnd.Should().Be(100000m);
        afterUnlock.Summary.TotalUnlockRevenueVnd.Should().Be(15000m);
        afterUnlock.Summary.TotalRevenueVnd.Should().Be(15000m);
    }

    // ─── AUD-BE-10: mã đơn, ngôn ngữ, giá kỳ vọng, phạm vi duyệt ────────────

    [Fact]
    public async Task OrderCodeGenerator_GeneratesUniqueTenDigitCodes()
    {
        var codes = new HashSet<string>();
        for (var i = 0; i < 20; i++)
        {
            var code = await PanelForge.Application.Features.Payments.Common.OrderCodeGenerator.GenerateUniqueAsync(_db, default);
            code.Should().MatchRegex(@"^PF\d{10}$");
            codes.Add(code).Should().BeTrue();
        }
    }

    [Fact]
    public async Task SwitchingEditionLanguage_CreatesSeparatePendingOrder_DoesNotReuseOtherLanguage()
    {
        TestEditions.Monetize(_db, _paidChapter.Id, isPaid: true, priceVnd: 15000m, language: "en");
        await _db.SaveChangesAsync();

        var viOrder = await CreateOrder(new TopupRequest(ChapterId: _paidChapter.Id, Purpose: "ChapterUnlock", Language: "vi"));
        var enOrder = await CreateOrder(new TopupRequest(ChapterId: _paidChapter.Id, Purpose: "ChapterUnlock", Language: "en"));

        viOrder.IsSuccess.Should().BeTrue();
        enOrder.IsSuccess.Should().BeTrue();
        enOrder.Value!.OrderId.Should().NotBe(viOrder.Value!.OrderId);

        var txs = await _db.PaymentTransactions.ToListAsync();
        txs.Should().HaveCount(2);
        txs.Select(t => t.Language).Should().BeEquivalentTo(["vi", "en"]);
    }

    [Fact]
    public async Task UnlockChapter_WithMismatchedExpectedPriceVnd_ReturnsPriceChanged_AndDoesNotDeduct()
    {
        _db.ReaderWallets.Add(ReaderWallet.Create(_readerId, 50000m));
        await _db.SaveChangesAsync();

        var result = await new UnlockChapterCommandHandler(_db, new DefaultBankSettings())
            .Handle(new UnlockChapterCommand(_readerId, new UnlockChapterRequest(_paidChapter.Id, ExpectedPriceVnd: 10000)), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.PriceChanged);
        (await _db.ReaderWallets.SingleAsync(w => w.UserId == _readerId)).BalanceVnd.Should().Be(50000m);
        (await _db.ChapterEntitlements.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ApproveAndRejectPayment_RejectsTransactionsWithNullOrDifferentWorkspaceId()
    {
        var nullWsTx = PaymentTransaction.Create(
            _readerId, "PF9999990001", PaymentTransactionType.Topup, 20000m, "", "970422", "0123", "TEST",
            workspaceId: null);
        var otherWsTx = PaymentTransaction.Create(
            _readerId, "PF9999990002", PaymentTransactionType.Topup, 20000m, "", "970422", "0123", "TEST",
            workspaceId: Guid.NewGuid());
        _db.PaymentTransactions.AddRange(nullWsTx, otherWsTx);
        await _db.SaveChangesAsync();

        var approveHandler = new ApprovePaymentCommandHandler(_db);
        var rejectHandler = new PanelForge.Application.Features.Payments.Commands.RejectPayment.RejectPaymentCommandHandler(_db);

        (await approveHandler.Handle(new ApprovePaymentCommand(_studio.Id, nullWsTx.Id, _producerId), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        (await approveHandler.Handle(new ApprovePaymentCommand(_studio.Id, otherWsTx.Id, _producerId), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        (await rejectHandler.Handle(new(_studio.Id, nullWsTx.Id, _producerId, "nope"), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        (await rejectHandler.Handle(new(_studio.Id, otherWsTx.Id, _producerId, "nope"), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }
}
