using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Catalog;
using PanelForge.Application.Features.Payments.Common;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Commands.CreateTopup;

/// <summary>
/// Tạo đơn chuyển khoản VietQR ở trạng thái Pending vào tài khoản ngân hàng nền tảng (Q13); Publisher đối soát và duyệt.
/// Purpose: Topup (nạp ví) | ChapterUnlock (mở chương trực tiếp). Gói VIP đã gỡ (Q4, OPS-11).
/// Với ChapterUnlock, số tiền và Workspace do server quyết định — không tin giá client gửi.
/// </summary>
public sealed record CreateTopupCommand(
    Guid UserId,
    TopupRequest Request
) : IRequest<Result<TopupResponse>>;

public sealed class CreateTopupCommandHandler : IRequestHandler<CreateTopupCommand, Result<TopupResponse>>
{
    private const int MinTopupVnd = 1000;
    private const int MaxTopupVnd = 50_000_000;
    private static readonly TimeSpan PendingReuseWindow = TimeSpan.FromMinutes(15);

    private readonly IPanelForgeDbContext _db;
    private readonly DefaultBankSettings _defaultBank;

    public CreateTopupCommandHandler(IPanelForgeDbContext db, DefaultBankSettings defaultBank)
    {
        _db = db;
        _defaultBank = defaultBank;
    }

    public async Task<Result<TopupResponse>> Handle(CreateTopupCommand command, CancellationToken ct)
    {
        var req = command.Request;

        if (!TryParsePurpose(req.Purpose, out var type))
            return Result<TopupResponse>.Failure("Loại giao dịch không hợp lệ.", ResultErrorCodes.Validation);

        var now = DateTime.UtcNow;
        Guid? workspaceId = req.WorkspaceId;
        Guid? seriesId = req.SeriesId;
        Guid? chapterId = null;
        string? language = null;
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
                    .Select(c => new { c.Id, c.SeriesId, c.Series.WorkspaceId, c.Status, c.PublishedAt })
                    .FirstOrDefaultAsync(ct);

                if (chapter == null)
                    return Result<TopupResponse>.Failure("Không tìm thấy chương truyện.", ResultErrorCodes.NotFound);

                if (chapter.Status != ChapterStatus.HandedOffAndPublished
                    || chapter.PublishedAt is null
                    || chapter.PublishedAt > now)
                {
                    return Result<TopupResponse>.Failure("Chương truyện chưa được phát hành.", ResultErrorCodes.NotFound);
                }

                language = ContentLanguages.Normalize(req.Language);
                if (language is null)
                    return Result<TopupResponse>.Failure("Ngôn ngữ không được hỗ trợ.", ResultErrorCodes.Validation);
                if (!(await ChapterReadAccess.AvailableLanguagesAsync(_db, chapter.Id, ct, now)).Contains(language))
                    return Result<TopupResponse>.Failure(ChapterReadAccess.LanguageUnavailableMessage, ResultErrorCodes.EditionNotPublished);

                // BR-33: giá lấy từ ấn bản ngôn ngữ đang mua (server là nguồn sự thật).
                var terms = await ChapterReadAccess.EditionTermsAsync(_db, chapter.Id, language, ct);
                if (!terms.IsPurchasable)
                    return Result<TopupResponse>.Failure(
                        terms.IsConfigured ? "Ấn bản này miễn phí, không cần thanh toán." : "Ấn bản này chưa được Publisher đặt giá.",
                        ResultErrorCodes.Validation);

                var alreadyUnlocked = await _db.ChapterEntitlements
                    .AnyAsync(e => e.UserId == command.UserId && e.ChapterId == chapter.Id && e.Language == language && !e.IsDeleted, ct);
                if (alreadyUnlocked)
                    return Result<TopupResponse>.Failure("Bạn đã mở khóa chương này trước đó.", ResultErrorCodes.Validation);

                chapterId = chapter.Id;
                seriesId = chapter.SeriesId;
                workspaceId = chapter.WorkspaceId;
                amountVnd = (int)terms.PriceVnd;
                break;
            }
            default:
                amountVnd = req.AmountVnd ?? (req.CoinsAmount.HasValue ? req.CoinsAmount.Value * 100 : 0);
                if (amountVnd < MinTopupVnd)
                    return Result<TopupResponse>.Failure("Số tiền nạp tối thiểu là 1.000 VNĐ.", ResultErrorCodes.Validation);
                if (amountVnd > MaxTopupVnd)
                    return Result<TopupResponse>.Failure("Số tiền nạp vượt quá giới hạn cho phép.", ResultErrorCodes.Validation);
                break;
        }

        // Q13: mọi tiền chuyển vào tài khoản ngân hàng của nền tảng. Đơn nạp Ví không thuộc Studio nào;
        // đơn mở chương gắn Workspace của chương để tính doanh thu Studio (BR-32).
        if (type != PaymentTransactionType.ChapterUnlock)
        {
            workspaceId = null;
            seriesId = null;
        }

        var bank = await PlatformBank.CurrentAsync(_db, _defaultBank, ct);
        string bankBin = bank.Bin;
        string bankName = bank.Name;
        string accountNo = bank.AccountNo;
        string accountName = bank.AccountName;

        // Chống spam đơn Pending: độc giả đóng/mở popup QR nhiều lần → dùng lại đơn Pending cùng loại/số tiền/chương
        // còn mới (≤ 15 phút, cùng tài khoản nhận); đơn Pending cũ hơn cùng loại được chuyển Expired để bảng Producer gọn.
        var reuseSince = now - PendingReuseWindow;
        var samePending = await _db.PaymentTransactions
            .Where(t => t.UserId == command.UserId
                        && t.Status == PaymentTransactionStatus.Pending
                        && t.Type == type
                        && t.ChapterId == chapterId
                        && t.Language == language
                        && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        var reusable = samePending.FirstOrDefault(t =>
            t.CreatedAt >= reuseSince
            && t.AmountVnd == amountVnd
            && t.WorkspaceId == workspaceId
            && t.Language == language
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

        // Mã đơn duy nhất dạng PF + 10 chữ số — cũng là nội dung chuyển khoản bắt buộc (AUD-BE-10).
        string orderCode = await OrderCodeGenerator.GenerateUniqueAsync(_db, ct);

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
            chapterId: chapterId,
            language: language
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

    private static bool TryParsePurpose(string? purpose, out PaymentTransactionType type)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            type = PaymentTransactionType.Topup;
            return true;
        }
        // Refund chỉ do Admin ghi (OPS-06); gói VIP đã gỡ (Q4, OPS-11); gói mở Studio chỉ mua bằng Ví (D11):
        // độc giả không tạo được đơn VietQR ba loại này
        return Enum.TryParse(purpose.Trim(), ignoreCase: true, out type) && Enum.IsDefined(type)
            && type is not (PaymentTransactionType.Refund or PaymentTransactionType.VipSubscription
                or PaymentTransactionType.StudioPackage or PaymentTransactionType.StudioResources);
    }
}
