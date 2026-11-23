using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Commands.VerifyGooglePlayPurchase;

/// <summary>
/// Bảng SKU gói nạp Ví trên Google Play Console (Consumable In-App Products).
/// Giá trị VNĐ do Backend quyết định tuyệt đối — không tin số tiền client gửi lên.
/// </summary>
public static class GooglePlayProductCatalog
{
    private static readonly Dictionary<string, int> Products = new(StringComparer.OrdinalIgnoreCase)
    {
        ["panelforge_topup_20k"] = 20_000,
        ["panelforge_topup_50k"] = 50_000,
        ["panelforge_topup_100k"] = 100_000,
        ["panelforge_topup_200k"] = 200_000,
        ["panelforge_topup_500k"] = 500_000,
    };

    public static bool TryGetAmountVnd(string? productId, out int amountVnd)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            amountVnd = 0;
            return false;
        }
        return Products.TryGetValue(productId.Trim(), out amountVnd);
    }

    public static IReadOnlyDictionary<string, int> All => Products;
}

public sealed record VerifyGooglePlayPurchaseRequest(
    string ProductId,
    string PurchaseToken,
    string? PackageName = null);

public sealed record VerifyGooglePlayPurchaseResponse(
    bool Success,
    int BalanceVnd,
    int CreditedVnd,
    string OrderCode,
    bool AlreadyProcessed,
    string Message);

public sealed record VerifyGooglePlayPurchaseCommand(
    Guid UserId,
    string ProductId,
    string PurchaseToken,
    string? PackageName = null)
    : IRequest<Result<VerifyGooglePlayPurchaseResponse>>;

public sealed class VerifyGooglePlayPurchaseCommandHandler
    : IRequestHandler<VerifyGooglePlayPurchaseCommand, Result<VerifyGooglePlayPurchaseResponse>>
{
    private const string ExpectedPackageName = "com.panelforge.reader";
    private readonly IPanelForgeDbContext _db;

    public VerifyGooglePlayPurchaseCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<VerifyGooglePlayPurchaseResponse>> Handle(
        VerifyGooglePlayPurchaseCommand command,
        CancellationToken ct)
    {
        if (!GooglePlayProductCatalog.TryGetAmountVnd(command.ProductId, out var amountVnd))
            return Result<VerifyGooglePlayPurchaseResponse>.Failure(
                $"Gói nạp '{command.ProductId}' không hợp lệ trên hệ thống.",
                ResultErrorCodes.Validation);

        var token = command.PurchaseToken?.Trim() ?? string.Empty;
        if (token.Length < 12)
            return Result<VerifyGooglePlayPurchaseResponse>.Failure(
                "Mã xác thực giao dịch Google Play (purchaseToken) không hợp lệ.",
                ResultErrorCodes.Validation);

        if (!string.IsNullOrWhiteSpace(command.PackageName)
            && !string.Equals(command.PackageName.Trim(), ExpectedPackageName, StringComparison.Ordinal))
        {
            return Result<VerifyGooglePlayPurchaseResponse>.Failure(
                "Package name không khớp ứng dụng PanelForge trên Google Play.",
                ResultErrorCodes.Validation);
        }

        // Idempotency key: mã hóa SHA-256 purchaseToken thành OrderCode duy nhất để chống cộng trùng
        var orderCode = BuildOrderCode(token);
        var existing = await _db.PaymentTransactions
            .FirstOrDefaultAsync(t => t.OrderCode == orderCode && !t.IsDeleted, ct);

        var wallet = await _db.ReaderWallets
            .FirstOrDefaultAsync(w => w.UserId == command.UserId && !w.IsDeleted, ct);

        if (existing is not null)
        {
            if (existing.UserId != command.UserId)
                return Result<VerifyGooglePlayPurchaseResponse>.Failure(
                    "Giao dịch Google Play này đã được sử dụng cho tài khoản khác.",
                    ResultErrorCodes.Conflict);

            return Result<VerifyGooglePlayPurchaseResponse>.Success(
                new VerifyGooglePlayPurchaseResponse(
                    Success: true,
                    BalanceVnd: (int)(wallet?.BalanceVnd ?? 0m),
                    CreditedVnd: 0,
                    OrderCode: existing.OrderCode,
                    AlreadyProcessed: true,
                    Message: "Giao dịch Google Play này đã được ghi nhận trước đó."));
        }

        if (wallet is null)
        {
            wallet = ReaderWallet.Create(command.UserId, 0m);
            _db.ReaderWallets.Add(wallet);
        }

        var tx = PaymentTransaction.Create(
            userId: command.UserId,
            orderCode: orderCode,
            type: PaymentTransactionType.Topup,
            amountVnd: amountVnd,
            qrImageUrl: string.Empty,
            bankBin: "GOOGLE_PLAY",
            bankAccountNo: command.ProductId.Trim(),
            bankAccountName: "Google Play Billing");
        tx.Approve(command.UserId, DateTime.UtcNow);
        _db.PaymentTransactions.Add(tx);

        wallet.AddBalance(amountVnd);

        _db.AuditLogs.Add(AuditLog.Create(
            action: "GOOGLE_PLAY_TOPUP",
            entityName: "PaymentTransaction",
            entityId: tx.Id.ToString(),
            userId: command.UserId,
            details: $"Nạp {amountVnd:N0} VNĐ qua Google Play Billing ({command.ProductId}, mã {orderCode})."));

        await _db.SaveChangesAsync(ct);

        return Result<VerifyGooglePlayPurchaseResponse>.Success(
            new VerifyGooglePlayPurchaseResponse(
                Success: true,
                BalanceVnd: (int)wallet.BalanceVnd,
                CreditedVnd: amountVnd,
                OrderCode: orderCode,
                AlreadyProcessed: false,
                Message: $"Đã nạp thành công {amountVnd:N0} VNĐ vào Ví qua Google Play."));
    }

    internal static string BuildOrderCode(string purchaseToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(purchaseToken));
        return "GP" + Convert.ToHexString(hash)[..16];
    }
}
