using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Payments.Commands.ReportTransactionIssue;
using PanelForge.Application.Features.Payments.Commands.VerifyGooglePlayPurchase;
using PanelForge.Application.Features.Payments.Queries.GetMyWallet;
using PanelForge.Infrastructure.ExternalServices.GooglePlay;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Payments;

/// <summary>
/// Nạp Ví qua Google Play Billing: chỉ cộng tiền khi Google Play Developer API xác nhận đã thanh toán,
/// không tin purchaseToken client gửi lên; báo lỗi giao dịch nhận cả Id lẫn OrderCode.
/// </summary>
public class GooglePlayTopupTests
{
    private const string Token = "real-looking-purchase-token-0001";
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _readerId = Guid.NewGuid();
    private readonly Mock<IGooglePlayPurchaseVerifier> _verifier = new();

    private void GoogleSays(GooglePlayPurchaseStatus status) =>
        _verifier
            .Setup(v => v.VerifyProductPurchaseAsync("com.panelforge.reader", "panelforge_topup_50k", Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GooglePlayPurchaseCheck(status, "GPA.1234", status == GooglePlayPurchaseStatus.Purchased ? null : "no"));

    private Task<Result<VerifyGooglePlayPurchaseResponse>> Verify(string productId = "panelforge_topup_50k", string token = Token) =>
        new VerifyGooglePlayPurchaseCommandHandler(_db, _verifier.Object)
            .Handle(new VerifyGooglePlayPurchaseCommand(_readerId, productId, token, "com.panelforge.reader"), default);

    private async Task<int> Balance() =>
        (int)((await _db.ReaderWallets.SingleOrDefaultAsync(w => w.UserId == _readerId))?.BalanceVnd ?? 0m);

    [Fact]
    public async Task Purchased_CreditsServerPrice_Once()
    {
        GoogleSays(GooglePlayPurchaseStatus.Purchased);

        var first = await Verify();
        var second = await Verify();

        first.IsSuccess.Should().BeTrue();
        first.Value!.CreditedVnd.Should().Be(50_000);
        second.Value!.AlreadyProcessed.Should().BeTrue();
        second.Value.CreditedVnd.Should().Be(0);
        (await Balance()).Should().Be(50_000);
    }

    [Theory]
    [InlineData(GooglePlayPurchaseStatus.Invalid, ResultErrorCodes.Validation)]
    [InlineData(GooglePlayPurchaseStatus.Canceled, ResultErrorCodes.Validation)]
    [InlineData(GooglePlayPurchaseStatus.Pending, ResultErrorCodes.Validation)]
    [InlineData(GooglePlayPurchaseStatus.NotConfigured, ResultErrorCodes.PaymentProviderUnavailable)]
    [InlineData(GooglePlayPurchaseStatus.Unavailable, ResultErrorCodes.PaymentProviderUnavailable)]
    public async Task NotConfirmedByGoogle_DoesNotCredit(GooglePlayPurchaseStatus status, string expectedCode)
    {
        GoogleSays(status);

        var result = await Verify();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(expectedCode);
        (await Balance()).Should().Be(0);
        (await _db.PaymentTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task FakeTokenWithoutVerifierMatch_DoesNotCredit()
    {
        // Verifier mock mặc định trả null → mọi token không được Google xác nhận đều bị từ chối.
        _verifier
            .Setup(v => v.VerifyProductPurchaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GooglePlayPurchaseCheck(GooglePlayPurchaseStatus.Invalid));

        var result = await Verify(productId: "panelforge_topup_500k", token: "totally-fake-token-xyz");

        result.IsSuccess.Should().BeFalse();
        (await Balance()).Should().Be(0);
    }

    [Fact]
    public async Task WalletHistory_ShowsGooglePlayTitle_AndOrderCode_UsableForDispute()
    {
        GoogleSays(GooglePlayPurchaseStatus.Purchased);
        var verified = await Verify();

        var wallet = await new GetMyWalletQueryHandler(_db).Handle(new GetMyWalletQuery(_readerId), default);
        var tx = wallet.Value!.Transactions.Single();
        tx.Title.Should().Contain("Google Play");
        tx.OrderCode.Should().Be(verified.Value!.OrderCode);

        var dispute = new ReportTransactionIssueCommandHandler(_db);
        (await dispute.Handle(new ReportTransactionIssueCommand(_readerId, tx.Id, "Chưa nhận được tiền"), default))
            .IsSuccess.Should().BeTrue("app cũ gửi Id giao dịch");
        (await dispute.Handle(new ReportTransactionIssueCommand(_readerId, tx.OrderCode, "Chưa nhận được tiền"), default))
            .IsSuccess.Should().BeTrue("app mới gửi OrderCode");
        (await dispute.Handle(new ReportTransactionIssueCommand(Guid.NewGuid(), tx.Id, "Chưa nhận được tiền"), default))
            .IsSuccess.Should().BeFalse("không được báo lỗi giao dịch của người khác");
    }

    [Theory]
    [InlineData("""{"purchaseState":0,"orderId":"GPA.1"}""", GooglePlayPurchaseStatus.Purchased)]
    [InlineData("""{"purchaseState":1}""", GooglePlayPurchaseStatus.Canceled)]
    [InlineData("""{"purchaseState":2}""", GooglePlayPurchaseStatus.Pending)]
    [InlineData("""{}""", GooglePlayPurchaseStatus.Invalid)]
    public void ParseProductPurchase_MapsPurchaseState(string json, GooglePlayPurchaseStatus expected)
    {
        using var doc = JsonDocument.Parse(json);
        GooglePlayPurchaseVerifier.ParseProductPurchase(doc.RootElement).Status.Should().Be(expected);
    }
}
