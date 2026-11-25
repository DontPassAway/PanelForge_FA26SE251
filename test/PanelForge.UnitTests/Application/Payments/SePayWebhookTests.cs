using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PanelForge.Application.Features.Payments.Commands.CreateTopup;
using PanelForge.Application.Features.Payments.Commands.ProcessSePayWebhook;
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

/// <summary>Webhook SePay tự duyệt đơn VietQR khi tiền vào đúng tài khoản, đủ số tiền, đúng mã đơn.</summary>
public class SePayWebhookTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly PanelForgeDbContext _db;
    private readonly Guid _readerId = Guid.NewGuid();
    private readonly StudioWorkspace _studio;
    private readonly Chapter _paidChapter;

    public SePayWebhookTests()
    {
        _db = InMemoryDb.Create(databaseName: _dbName);
        _studio = StudioWorkspace.Create("Studio Moon", Guid.NewGuid());
        _db.StudioWorkspaces.Add(_studio);
        var series = DomainSeries.Create(_studio.Id, "Moon Blade", ReadingDirection.RightToLeft, "Synopsis", "Fantasy");
        _db.Series.Add(series);
        _paidChapter = Chapter.Create(series.Id, 1, "Trả phí");
        _db.Chapters.Add(_paidChapter);
        TestEditions.Monetize(_db, _paidChapter.Id, isPaid: true, priceVnd: 15000m);
        _db.SaveChanges();
    }

    private async Task<TopupResponse> CreateOrder(TopupRequest request) =>
        (await new CreateTopupCommandHandler(_db).Handle(new CreateTopupCommand(_readerId, request), default)).Value!;

    private static SePayWebhookPayload Payload(TopupResponse order, long amount, string? content = null,
        string transferType = "in", string? account = null, string? subAccount = null) =>
        new(Id: 92704, Gateway: "MBBank", TransactionDate: "2026-11-23 10:00:00",
            AccountNumber: account ?? order.AccountNumber, Code: null,
            Content: content ?? $"MBVCB.123 {order.OrderId.ToLowerInvariant()} chuyen tien",
            TransferType: transferType, TransferAmount: amount, Accumulated: 0,
            SubAccount: subAccount, ReferenceCode: "MBVCB.3278907687", Description: "");

    private static async Task<SePayWebhookOutcome> Handle(PanelForgeDbContext db, SePayWebhookPayload payload) =>
        (await new ProcessSePayWebhookCommandHandler(db, NullLogger<ProcessSePayWebhookCommandHandler>.Instance)
            .Handle(new ProcessSePayWebhookCommand(payload), default)).Value;

    private Task<SePayWebhookOutcome> Webhook(TopupResponse order, long amount, string? content = null,
        string transferType = "in", string? account = null, string? subAccount = null) =>
        Handle(_db, Payload(order, amount, content, transferType, account, subAccount));

    [Fact]
    public async Task Topup_IsApprovedAndCreditsWallet_WhenCodeAmountAndAccountMatch()
    {
        var order = await CreateOrder(new TopupRequest(AmountVnd: 50000));

        (await Webhook(order, 50000)).Should().Be(SePayWebhookOutcome.Approved);

        var tx = await _db.PaymentTransactions.SingleAsync();
        tx.Status.Should().Be(PaymentTransactionStatus.Approved);
        tx.ConfirmedByUserId.Should().BeNull();
        (await _db.ReaderWallets.SingleAsync(w => w.UserId == _readerId)).BalanceVnd.Should().Be(50000m);
    }

    [Fact]
    public async Task ChapterUnlock_GrantsEntitlement()
    {
        var order = await CreateOrder(new TopupRequest(ChapterId: _paidChapter.Id, Purpose: "ChapterUnlock"));

        (await Webhook(order, 15000)).Should().Be(SePayWebhookOutcome.Approved);

        (await _db.ChapterEntitlements.AnyAsync(e => e.UserId == _readerId && e.ChapterId == _paidChapter.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateDelivery_DoesNotCreditTwice()
    {
        var order = await CreateOrder(new TopupRequest(AmountVnd: 20000));

        await Webhook(order, 20000);
        (await Webhook(order, 20000)).Should().Be(SePayWebhookOutcome.IgnoredAlreadyProcessed);

        (await _db.ReaderWallets.SingleAsync()).BalanceVnd.Should().Be(20000m);
    }

    [Fact]
    public async Task Underpayment_StaysPendingForManualReview()
    {
        var order = await CreateOrder(new TopupRequest(AmountVnd: 20000));

        (await Webhook(order, 19999)).Should().Be(SePayWebhookOutcome.IgnoredAmountTooLow);

        (await _db.PaymentTransactions.SingleAsync()).Status.Should().Be(PaymentTransactionStatus.Pending);
        (await _db.ReaderWallets.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task TransferToAnotherAccount_IsIgnored()
    {
        var order = await CreateOrder(new TopupRequest(AmountVnd: 20000));

        (await Webhook(order, 20000, account: "999999999")).Should().Be(SePayWebhookOutcome.IgnoredAccountMismatch);
        (await _db.PaymentTransactions.SingleAsync()).Status.Should().Be(PaymentTransactionStatus.Pending);
    }

    [Fact]
    public async Task OutgoingOrUnknownCode_IsIgnored()
    {
        var order = await CreateOrder(new TopupRequest(AmountVnd: 20000));

        (await Webhook(order, 20000, transferType: "out")).Should().Be(SePayWebhookOutcome.IgnoredNotIncoming);
        (await Webhook(order, 20000, content: "chuyen tien an trua")).Should().Be(SePayWebhookOutcome.IgnoredNoOrderCode);
        (await Webhook(order, 20000, content: "PF000001")).Should().Be(SePayWebhookOutcome.IgnoredOrderNotFound);
        (await _db.PaymentTransactions.SingleAsync()).Status.Should().Be(PaymentTransactionStatus.Pending);
    }

    [Fact]
    public async Task ExpiredOrder_IsReopenedAndApproved_WhenMoneyArrives()
    {
        var order = await CreateOrder(new TopupRequest(AmountVnd: 20000));
        (await _db.PaymentTransactions.SingleAsync()).Expire();
        await _db.SaveChangesAsync();

        (await Webhook(order, 20000)).Should().Be(SePayWebhookOutcome.Approved);

        (await _db.PaymentTransactions.SingleAsync()).Status.Should().Be(PaymentTransactionStatus.Approved);
        (await _db.ReaderWallets.SingleAsync()).BalanceVnd.Should().Be(20000m);
    }

    [Fact]
    public async Task VirtualSubAccount_MatchingOrderAccount_IsApproved()
    {
        var order = await CreateOrder(new TopupRequest(AmountVnd: 20000));

        (await Webhook(order, 20000, account: "999999999", subAccount: order.AccountNumber))
            .Should().Be(SePayWebhookOutcome.Approved);
    }

    [Fact]
    public async Task ConcurrentDelivery_SecondSaveIsRejected_WalletCreditedOnce()
    {
        var order = await CreateOrder(new TopupRequest(AmountVnd: 20000));
        _db.ReaderWallets.Add(ReaderWallet.Create(_readerId));
        await _db.SaveChangesAsync();

        // Hai request SePay (bản gốc + bản gửi lại) cùng đọc đơn Pending trước khi bên nào kịp lưu.
        await using var first = InMemoryDb.Create(databaseName: _dbName);
        await using var second = InMemoryDb.Create(databaseName: _dbName);
        await second.PaymentTransactions.SingleAsync();
        await second.ReaderWallets.SingleAsync();

        (await Handle(first, Payload(order, 20000))).Should().Be(SePayWebhookOutcome.Approved);
        var late = () => Handle(second, Payload(order, 20000));
        await late.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var check = InMemoryDb.Create(databaseName: _dbName);
        (await check.ReaderWallets.SingleAsync()).BalanceVnd.Should().Be(20000m);
        (await check.PaymentTransactions.SingleAsync()).Status.Should().Be(PaymentTransactionStatus.Approved);
    }

    [Fact]
    public async Task WebhookAndManualSpendAtOnce_DoNotLoseWalletUpdate()
    {
        _db.ReaderWallets.Add(ReaderWallet.Create(_readerId, 30000m));
        await _db.SaveChangesAsync();
        var order = await CreateOrder(new TopupRequest(AmountVnd: 20000));

        // Độc giả trừ ví (mở chương) đúng lúc webhook đang cộng tiền: lần lưu sau không được ghi đè số dư.
        await using var spend = InMemoryDb.Create(databaseName: _dbName);
        (await spend.ReaderWallets.SingleAsync()).DeductBalance(10000m);
        (await Handle(_db, Payload(order, 20000))).Should().Be(SePayWebhookOutcome.Approved);

        var save = () => spend.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var check = InMemoryDb.Create(databaseName: _dbName);
        (await check.ReaderWallets.SingleAsync()).BalanceVnd.Should().Be(50000m);
    }
}
