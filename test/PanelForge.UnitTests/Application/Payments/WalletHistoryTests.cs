using FluentAssertions;
using PanelForge.Application.Features.Payments.Queries.GetMyWallet;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Payments;

/// <summary>
/// Lịch sử Ví (GET wallet): chỉ giao dịch Approved làm đổi số dư Ví, mỗi dòng có số dư sau giao dịch thật.
/// </summary>
public class WalletHistoryTests
{
    private const string Qr = "https://img.vietqr.io/image/970422-0123456789-compact2.png";
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _readerId = Guid.NewGuid();
    private readonly DateTime _t0 = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

    private PaymentTransaction Tx(string code, PaymentTransactionType type, decimal amount, string qr = Qr, string bankBin = "970422")
    {
        var tx = PaymentTransaction.Create(_readerId, code, type, amount, qr, bankBin, "0123456789", "KOMAFORGE STUDIO");
        _db.PaymentTransactions.Add(tx);
        return tx;
    }

    private async Task<PanelForge.Application.Features.Payments.Models.UserWalletDto> Wallet()
    {
        var result = await new GetMyWalletQueryHandler(_db).Handle(new GetMyWalletQuery(_readerId), default);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    [Fact]
    public async Task PendingTopup_IsNotShownAsCompleted()
    {
        _db.ReaderWallets.Add(ReaderWallet.Create(_readerId, 0m));
        Tx("PF700000", PaymentTransactionType.Topup, 70_000);
        await _db.SaveChangesAsync();

        var wallet = await Wallet();

        wallet.BalanceVnd.Should().Be(0);
        wallet.Transactions.Should().BeEmpty("đơn nạp chưa được duyệt không làm đổi số dư");
    }

    [Fact]
    public async Task OnlyApprovedWalletMovements_WithRunningBalanceAfter()
    {
        // Số dư hiện tại = 70.000 − 20.000 + 10.000.
        _db.ReaderWallets.Add(ReaderWallet.Create(_readerId, 60_000m));

        Tx("PF100001", PaymentTransactionType.Topup, 70_000).Approve(Guid.NewGuid(), _t0);
        Tx("PF100002", PaymentTransactionType.ChapterUnlock, 20_000, qr: "").Approve(_readerId, _t0.AddHours(1));
        Tx("PF100003", PaymentTransactionType.Topup, 10_000, qr: "", bankBin: "GOOGLE_PLAY").Approve(_readerId, _t0.AddHours(2));

        // Không đi qua Ví / chưa thành công → không có trong lịch sử.
        Tx("PF100004", PaymentTransactionType.Topup, 50_000);                                   // Pending
        Tx("PF100005", PaymentTransactionType.Topup, 30_000).Reject(Guid.NewGuid(), "sai");    // Rejected
        var expired = Tx("PF100006", PaymentTransactionType.Topup, 40_000);
        expired.Expire();                                                                       // Expired
        Tx("PF100007", PaymentTransactionType.VipSubscription, 19_900).Approve(null, _t0.AddHours(3)); // VietQR trực tiếp
        await _db.SaveChangesAsync();

        var wallet = await Wallet();

        wallet.BalanceVnd.Should().Be(60_000);
        wallet.Transactions.Select(t => (t.OrderCode, t.AmountVnd, t.BalanceAfter)).Should().Equal(
            ("PF100003", 10_000, 60_000),
            ("PF100002", -20_000, 50_000),
            ("PF100001", 70_000, 70_000));
        wallet.Transactions[0].CreatedAt.Should().Be(_t0.AddHours(2));
    }

    [Fact]
    public async Task TopupApprovedLater_IsOrderedByApprovalTime()
    {
        // 30.000 → mở chương −20.000 → đơn VietQR 50.000 (tạo trước lần mở chương nhưng duyệt sau) = 60.000.
        _db.ReaderWallets.Add(ReaderWallet.Create(_readerId, 60_000m));
        Tx("PF200000", PaymentTransactionType.Topup, 30_000).Approve(Guid.NewGuid(), _t0.AddHours(-1));
        var lateTopup = Tx("PF200001", PaymentTransactionType.Topup, 50_000);
        await _db.SaveChangesAsync();

        Tx("PF200002", PaymentTransactionType.ChapterUnlock, 20_000, qr: "").Approve(_readerId, _t0);
        await _db.SaveChangesAsync();

        lateTopup.Approve(Guid.NewGuid(), _t0.AddHours(1));
        await _db.SaveChangesAsync();

        var wallet = await Wallet();

        wallet.Transactions.Select(t => (t.OrderCode, t.BalanceAfter)).Should().Equal(
            ("PF200001", 60_000),
            ("PF200002", 10_000),
            ("PF200000", 30_000));
    }

    // ─── AUD-BE-05: GET /api/wallet/transactions/{orderCode}/status ──────────

    [Fact]
    public async Task GetTransactionStatus_OwnerGetsStatus_OtherUserGetsNotFound_PrivilegedGetsStatus()
    {
        Tx("PF555123", PaymentTransactionType.Topup, 50_000).Approve(Guid.NewGuid(), _t0);
        await _db.SaveChangesAsync();

        var handler = new PanelForge.Application.Features.Payments.Queries.GetTransactionStatus.GetTransactionStatusQueryHandler(_db);

        var ownerResult = await handler.Handle(new("PF555123", _readerId, IsPrivileged: false), default);
        ownerResult.IsSuccess.Should().BeTrue();
        ownerResult.Value!.Status.Should().Be("Approved");
        ownerResult.Value.IsCompleted.Should().BeTrue();
        ownerResult.Value.AmountVnd.Should().Be(50_000);

        var otherUserResult = await handler.Handle(new("PF555123", Guid.NewGuid(), IsPrivileged: false), default);
        otherUserResult.IsSuccess.Should().BeFalse();
        otherUserResult.ErrorCode.Should().Be(PanelForge.Application.Common.ResultErrorCodes.NotFound);

        var staffResult = await handler.Handle(new("PF555123", Guid.NewGuid(), IsPrivileged: true), default);
        staffResult.IsSuccess.Should().BeTrue();
        staffResult.Value!.OrderCode.Should().Be("PF555123");
    }

    [Fact]
    public void WalletController_GetTransactionStatus_RequiresAuthentication_NotAllowAnonymous()
    {
        var controllerType = typeof(PanelForge.API.Controllers.WalletController);
        controllerType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Should().NotBeEmpty();

        var method = controllerType.GetMethod(nameof(PanelForge.API.Controllers.WalletController.GetTransactionStatus))!;
        method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: true)
            .Should().BeEmpty("endpoint must require authentication (AUD-BE-05)");
    }
}
