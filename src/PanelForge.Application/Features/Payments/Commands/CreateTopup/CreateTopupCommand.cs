using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Commands.CreateTopup;

/// <summary>
/// Tạo đơn chuyển khoản VietQR ở trạng thái Pending (hiển thị ngay trên màn hình Thanh toán của Producer).
/// Purpose: Topup (nạp ví) | ChapterUnlock (mở chương trực tiếp) | VipSubscription (gói VIP trực tiếp).
/// Với ChapterUnlock / VipSubscription, số tiền và Workspace do server quyết định — không tin giá client gửi.
/// </summary>
public sealed record CreateTopupCommand(
    Guid UserId,
    TopupRequest Request
) : IRequest<Result<TopupResponse>>;

public sealed class CreateTopupCommandHandler : IRequestHandler<CreateTopupCommand, Result<TopupResponse>>
{
    private const int MinTopupVnd = 1000;
    private const int MaxTopupVnd = 50_000_000;
    private const string DefaultStudioName = "komaforge";
    private static readonly TimeSpan PendingReuseWindow = TimeSpan.FromMinutes(15);

    private readonly IPanelForgeDbContext _db;

    public CreateTopupCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<TopupResponse>> Handle(CreateTopupCommand command, CancellationToken ct)
    {
        var req = command.Request;

        if (!TryParsePurpose(req.Purpose, out var type))
            return Result<TopupResponse>.Failure("Loại giao dịch không hợp lệ.", ResultErrorCodes.Validation);

        Guid? workspaceId = req.WorkspaceId;
        Guid? seriesId = req.SeriesId;
        Guid? chapterId = null;
        int amountVnd;

        switch (type)
        {
            case PaymentTransactionType.ChapterUnlock:
            {
                if (!req.ChapterId.HasValue)
                    return Result<TopupResponse>.Failure("Thiếu chương cần mở khóa.", ResultErrorCodes.Validation);

                var chapter = await _db.Chapters
                    .AsNoTracking()
                    .Where(c => c.Id == req.ChapterId.Value && !c.IsDeleted)
                    .Select(c => new { c.Id, c.SeriesId, c.Series.WorkspaceId, c.IsLocked, c.PriceVnd })
                    .FirstOrDefaultAsync(ct);

                if (chapter == null)
                    return Result<TopupResponse>.Failure("Không tìm thấy chương truyện.", ResultErrorCodes.NotFound);
                if (!chapter.IsLocked || chapter.PriceVnd <= 0)
                    return Result<TopupResponse>.Failure("Chương này miễn phí, không cần thanh toán.", ResultErrorCodes.Validation);

                var alreadyUnlocked = await _db.ChapterEntitlements
                    .AnyAsync(e => e.UserId == command.UserId && e.ChapterId == chapter.Id && !e.IsDeleted, ct);
                if (alreadyUnlocked)
                    return Result<TopupResponse>.Failure("Bạn đã mở khóa chương này trước đó.", ResultErrorCodes.Validation);

                chapterId = chapter.Id;
                seriesId = chapter.SeriesId;
                workspaceId = chapter.WorkspaceId;
                amountVnd = (int)chapter.PriceVnd;
                break;
            }
            case PaymentTransactionType.VipSubscription:
                amountVnd = VipPricing.CostVnd(req.VipPlan);
                break;
            default:
                amountVnd = req.AmountVnd ?? (req.CoinsAmount.HasValue ? req.CoinsAmount.Value * 100 : 0);
                if (amountVnd < MinTopupVnd)
                    return Result<TopupResponse>.Failure("Số tiền nạp tối thiểu là 1.000 VNĐ.", ResultErrorCodes.Validation);
                if (amountVnd > MaxTopupVnd)
                    return Result<TopupResponse>.Failure("Số tiền nạp vượt quá giới hạn cho phép.", ResultErrorCodes.Validation);
                break;
        }

        // Suy ra Workspace (Studio nhận tiền) từ Series nếu client chỉ gửi ngữ cảnh bộ truyện.
        if (seriesId.HasValue && !workspaceId.HasValue)
        {
            workspaceId = await _db.Series
                .AsNoTracking()
                .Where(s => s.Id == seriesId.Value)
                .Select(s => (Guid?)s.WorkspaceId)
                .FirstOrDefaultAsync(ct);
        }

        // Mọi đơn VietQR phải thuộc một Studio có thật để Producer thấy và duyệt được trên /workspaces/{id}/payments.
        // Không có (hoặc sai) ngữ cảnh → fallback Studio mặc định (ưu tiên KomaForge Studio).
        var ws = workspaceId.HasValue
            ? await _db.StudioWorkspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workspaceId.Value && !w.IsDeleted, ct)
            : null;
        ws ??= await FindDefaultStudioAsync(ct);
        if (ws is null)
            return Result<TopupResponse>.Failure("Hệ thống chưa có Studio nào nhận thanh toán. Vui lòng thử lại sau.", ResultErrorCodes.Validation);
        workspaceId = ws.Id;

        // Tài khoản thụ hưởng: của Studio nếu đã cấu hình, ngược lại dùng tài khoản mặc định của hệ thống.
        string bankBin = "970422";
        string bankName = "MB Bank";
        string accountNo = "0123456789";
        string accountName = "KOMAFORGE STUDIO";

        if (!string.IsNullOrWhiteSpace(ws.BankBin) && !string.IsNullOrWhiteSpace(ws.BankAccountNo))
        {
            bankBin = ws.BankBin;
            bankName = ws.BankName ?? bankName;
            accountNo = ws.BankAccountNo;
            accountName = ws.BankAccountName ?? accountName;
        }

        // Chống spam đơn Pending: độc giả đóng/mở popup QR nhiều lần → dùng lại đơn Pending cùng loại/số tiền/chương
        // còn mới (≤ 15 phút, cùng tài khoản nhận); đơn Pending cũ hơn cùng loại được chuyển Expired để bảng Producer gọn.
        var now = DateTime.UtcNow;
        var reuseSince = now - PendingReuseWindow;
        var samePending = await _db.PaymentTransactions
            .Where(t => t.UserId == command.UserId
                        && t.Status == PaymentTransactionStatus.Pending
                        && t.Type == type
                        && t.ChapterId == chapterId
                        && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        var reusable = samePending.FirstOrDefault(t =>
            t.CreatedAt >= reuseSince
            && t.AmountVnd == amountVnd
            && t.WorkspaceId == workspaceId
            && t.BankBin == bankBin
            && t.BankAccountNo == accountNo);

        var stale = samePending.Where(t => t != reusable).ToList();
        foreach (var old in stale) old.Expire();
        if (stale.Count > 0) await _db.SaveChangesAsync(ct);

        if (reusable is not null)
        {
            return Result<TopupResponse>.Success(new TopupResponse(
                OrderId: reusable.OrderCode,
                Coins: amountVnd / 100,
                AmountVnd: amountVnd,
                QrCodeUrl: reusable.QrImageUrl,
                BankName: bankName,
                AccountNumber: reusable.BankAccountNo,
                AccountName: reusable.BankAccountName,
                TransferContent: reusable.OrderCode,
                IsCompleted: false,
                Purpose: type.ToString(),
                Status: PaymentTransactionStatus.Pending.ToString()
            ));
        }

        // Mã đơn duy nhất dạng PF + 6 chữ số — cũng là nội dung chuyển khoản bắt buộc.
        string orderCode;
        int attempts = 0;
        do
        {
            orderCode = $"PF{Random.Shared.Next(100000, 999999)}";
            attempts++;
        }
        while (await _db.PaymentTransactions.AnyAsync(t => t.OrderCode == orderCode, ct) && attempts < 10);

        string qrImageUrl =
            $"https://img.vietqr.io/image/{bankBin}-{accountNo}-compact2.png?amount={amountVnd}&addInfo={orderCode}&accountName={Uri.EscapeDataString(accountName)}";

        var transaction = PaymentTransaction.Create(
            userId: command.UserId,
            orderCode: orderCode,
            type: type,
            amountVnd: amountVnd,
            qrImageUrl: qrImageUrl,
            bankBin: bankBin,
            bankAccountNo: accountNo,
            bankAccountName: accountName,
            workspaceId: workspaceId,
            seriesId: seriesId,
            chapterId: chapterId
        );

        _db.PaymentTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);

        return Result<TopupResponse>.Success(new TopupResponse(
            OrderId: orderCode,
            Coins: amountVnd / 100,
            AmountVnd: amountVnd,
            QrCodeUrl: qrImageUrl,
            BankName: bankName,
            AccountNumber: accountNo,
            AccountName: accountName,
            TransferContent: orderCode,
            IsCompleted: false,
            Purpose: type.ToString(),
            Status: PaymentTransactionStatus.Pending.ToString()
        ));
    }

    /// <summary>Studio nhận đơn khi client không gửi ngữ cảnh: ưu tiên "KomaForge Studio", rồi Studio đã cấu hình ngân hàng, rồi Studio cũ nhất.</summary>
    private Task<StudioWorkspace?> FindDefaultStudioAsync(CancellationToken ct) =>
        _db.StudioWorkspaces.AsNoTracking()
            .Where(w => !w.IsDeleted)
            .OrderByDescending(w => w.Name.ToLower().Contains(DefaultStudioName))
            .ThenByDescending(w => w.BankBin != null && w.BankAccountNo != null)
            .ThenBy(w => w.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private static bool TryParsePurpose(string? purpose, out PaymentTransactionType type)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            type = PaymentTransactionType.Topup;
            return true;
        }
        return Enum.TryParse(purpose.Trim(), ignoreCase: true, out type) && Enum.IsDefined(type);
    }
}
