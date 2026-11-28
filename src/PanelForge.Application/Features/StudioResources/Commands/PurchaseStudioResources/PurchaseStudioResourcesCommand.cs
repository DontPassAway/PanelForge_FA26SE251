using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Common;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Application.Features.StudioResources.Common;
using PanelForge.Application.Features.StudioResources.Models;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.StudioResources.Commands.PurchaseStudioResources;

/// <summary>
/// POST /api/wallet/studio-resources — Producer mua thêm dung lượng / token AI bằng Ví theo đơn giá Admin đặt (D12).
/// Mua một lần, cộng ngay vào kho; dùng hết thì mua thêm. Giá do server tính; client thấy tổng khác → 409.
/// </summary>
public sealed record PurchaseStudioResourcesCommand(Guid UserId, int StorageGb, int TokenMillions, int ExpectedTotalVnd)
    : IRequest<Result<PurchaseStudioResourcesResponse>>;

public sealed class PurchaseStudioResourcesCommandHandler
    : IRequestHandler<PurchaseStudioResourcesCommand, Result<PurchaseStudioResourcesResponse>>
{
    public const int MaxUnitsPerOrder = 1_000;

    private readonly IPanelForgeDbContext _db;
    private readonly DefaultBankSettings _defaultBank;
    private readonly WorkspaceDefaultsSettings _defaults;

    public PurchaseStudioResourcesCommandHandler(
        IPanelForgeDbContext db, DefaultBankSettings defaultBank, WorkspaceDefaultsSettings defaults)
    {
        _db = db;
        _defaultBank = defaultBank;
        _defaults = defaults;
    }

    public async Task<Result<PurchaseStudioResourcesResponse>> Handle(PurchaseStudioResourcesCommand command, CancellationToken ct)
    {
        if (command.StorageGb < 0 || command.TokenMillions < 0 || command.StorageGb + command.TokenMillions == 0
            || command.StorageGb > MaxUnitsPerOrder || command.TokenMillions > MaxUnitsPerOrder)
            return Failure($"Chọn số GB hoặc số triệu token cần mua (0 – {MaxUnitsPerOrder:N0} mỗi loại).", ResultErrorCodes.Validation);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null || !user.IsActive)
            return Failure("Người dùng không tồn tại hoặc đã bị vô hiệu hóa.", ResultErrorCodes.Unauthorized);
        if (!user.CanCreateStudio)
            return Failure("Cần mua gói Mở Studio trước khi mua thêm tài nguyên.", ResultErrorCodes.Forbidden);

        var price = await StudioResourceLedger.CurrentPriceAsync(_db, ct);
        if (price is null)
            return Failure("Tài nguyên Studio chưa được mở bán. Liên hệ Administrator.", ResultErrorCodes.NotFound);

        var total = (long)command.StorageGb * price.PricePerGbVnd + (long)command.TokenMillions * price.PricePerMillionTokensVnd;
        if (total > int.MaxValue)
            return Failure("Đơn hàng quá lớn, vui lòng chia nhỏ.", ResultErrorCodes.Validation);
        if (command.ExpectedTotalVnd != total)
            return Failure(
                $"Giá đã thay đổi (kỳ vọng {command.ExpectedTotalVnd:N0}đ, hiện tại {total:N0}đ). Vui lòng tải lại.",
                ResultErrorCodes.PriceChanged);

        var wallet = await _db.ReaderWallets.FirstOrDefaultAsync(w => w.UserId == user.Id && !w.IsDeleted, ct);
        var balance = (long)(wallet?.BalanceVnd ?? 0m);
        if (wallet is null || balance < total)
            return Failure(
                $"Số dư trong Ví không đủ (thiếu {total - balance:N0}đ). Vui lòng nạp thêm.",
                ResultErrorCodes.Validation);

        wallet.DeductBalance((int)total);

        var orderCode = await OrderCodeGenerator.GenerateUniqueAsync(_db, ct);
        var bank = await PlatformBank.CurrentAsync(_db, _defaultBank, ct);
        var transaction = PaymentTransaction.Create(
            userId: user.Id,
            orderCode: orderCode,
            type: Domain.Enums.PaymentTransactionType.StudioResources,
            amountVnd: (int)total,
            qrImageUrl: "",
            bankBin: bank.Bin,
            bankAccountNo: bank.AccountNo,
            bankAccountName: bank.AccountName);
        transaction.Approve(user.Id, DateTime.UtcNow);
        _db.PaymentTransactions.Add(transaction);

        var pool = await StudioResourceLedger.EnsurePoolAsync(_db, user.Id, ct);
        pool.Add(command.StorageGb * StudioResourceLedger.BytesPerGb, command.TokenMillions * StudioResourceLedger.TokensPerUnit);

        _db.AuditLogs.Add(AuditLog.Create(
            action: "STUDIO_RESOURCES_PURCHASED",
            entityName: nameof(StudioResourcePool),
            entityId: pool.Id.ToString(),
            userId: user.Id,
            details: $"Mua thêm {command.StorageGb} GB và {command.TokenMillions} triệu token bằng Ví, {total:N0}đ ({transaction.OrderCode})."));

        // Ví (updated_at) và kho (xmin) là concurrency token: hai lần mua cùng lúc → lần sau 409, không trừ trùng.
        await _db.SaveChangesAsync(ct);

        return Result<PurchaseStudioResourcesResponse>.Success(new PurchaseStudioResourcesResponse(
            (int)total, (int)wallet.BalanceVnd, transaction.OrderCode,
            await StudioResourceLedger.LoadAsync(_db, _defaults, user.Id, ct)));
    }

    private static Result<PurchaseStudioResourcesResponse> Failure(string message, string code) =>
        Result<PurchaseStudioResourcesResponse>.Failure(message, code);
}
