using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Catalog;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Commands.UnlockChapter;

public sealed record UnlockChapterCommand(
    Guid UserId,
    UnlockChapterRequest Request
) : IRequest<Result<UnlockChapterResponse>>;

public sealed class UnlockChapterCommandHandler : IRequestHandler<UnlockChapterCommand, Result<UnlockChapterResponse>>
{
    private readonly IPanelForgeDbContext _db;

    public UnlockChapterCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<UnlockChapterResponse>> Handle(UnlockChapterCommand command, CancellationToken ct)
    {
        var req = command.Request;
        var language = ContentLanguages.Normalize(req.Language);
        if (language is null)
            return Result<UnlockChapterResponse>.Failure("Ngôn ngữ không được hỗ trợ.", ResultErrorCodes.Validation);
        var chapter = await _db.Chapters
            .Include(c => c.Series)
            .FirstOrDefaultAsync(c => c.Id == req.ChapterId && !c.IsDeleted, ct);

        if (chapter == null)
            return Result<UnlockChapterResponse>.Failure("Không tìm thấy chương truyện.", ResultErrorCodes.NotFound);

        if (!(await ChapterReadAccess.AvailableLanguagesAsync(_db, chapter.Id, ct)).Contains(language))
            return Result<UnlockChapterResponse>.Failure(ChapterReadAccess.LanguageUnavailableMessage, ResultErrorCodes.Validation);

        var wallet = await _db.ReaderWallets
            .FirstOrDefaultAsync(w => w.UserId == command.UserId && !w.IsDeleted, ct);

        if (wallet == null)
        {
            wallet = ReaderWallet.Create(command.UserId, 0m);
            _db.ReaderWallets.Add(wallet);
        }

        bool isVip = wallet.IsVip && (wallet.VipExpiresAt == null || wallet.VipExpiresAt > DateTime.UtcNow);

        // Đã mở khóa trước đó
        var alreadyUnlocked = await _db.ChapterEntitlements
            .AnyAsync(e => e.UserId == command.UserId && e.ChapterId == chapter.Id && e.Language == language && !e.IsDeleted, ct);

        if (alreadyUnlocked)
        {
            var balVnd = (int)wallet.BalanceVnd;
            return Result<UnlockChapterResponse>.Success(new UnlockChapterResponse(
                ChapterId: chapter.Id.ToString(),
                Success: true,
                RemainingCoins: balVnd / 100,
                RemainingBalanceVnd: balVnd,
                Message: "Chương này đã được mở khóa trước đó.",
                IsPermanent: true
            ));
        }

        // Trường hợp chương miễn phí hoặc có đặc quyền VIP
        if (!chapter.IsLocked || isVip)
        {
            var freeEntitlement = ChapterEntitlement.Create(
                userId: command.UserId,
                chapterId: chapter.Id,
                seriesId: chapter.SeriesId,
                workspaceId: chapter.Series.WorkspaceId,
                pricePaidVnd: 0m,
                language: language
            );
            _db.ChapterEntitlements.Add(freeEntitlement);
            await _db.SaveChangesAsync(ct);

            var balVnd = (int)wallet.BalanceVnd;
            string msg = isVip
                ? "Đặc quyền VIP: Mở khóa chương truyện miễn phí!"
                : "Chương truyện miễn phí, đã mở khóa thành công!";

            return Result<UnlockChapterResponse>.Success(new UnlockChapterResponse(
                ChapterId: chapter.Id.ToString(),
                Success: true,
                RemainingCoins: balVnd / 100,
                RemainingBalanceVnd: balVnd,
                Message: msg,
                IsPermanent: true
            ));
        }

        // Trường hợp chương trả phí cần trừ tiền ví
        // Giá luôn lấy từ chương (server là nguồn sự thật) — không tin giá client gửi lên.
        const int defaultLockedPriceVnd = 10000;
        var languagePrice = await ChapterReadAccess.ChapterPriceAsync(_db, chapter.Id, chapter.PriceVnd, language, ct);
        int priceVnd = languagePrice > 0 ? (int)languagePrice : defaultLockedPriceVnd;

        if (wallet.BalanceVnd < priceVnd)
        {
            int missing = priceVnd - (int)wallet.BalanceVnd;
            return Result<UnlockChapterResponse>.Failure(
                $"Số dư trong Ví không đủ để mở khóa chương (thiếu {missing:N0}đ). Vui lòng nạp thêm.",
                ResultErrorCodes.Validation
            );
        }

        wallet.DeductBalance(priceVnd);

        string orderCode = $"PF{Random.Shared.Next(100000, 999999)}";
        var transaction = PaymentTransaction.Create(
            userId: command.UserId,
            orderCode: orderCode,
            type: PaymentTransactionType.ChapterUnlock,
            amountVnd: priceVnd,
            qrImageUrl: "",
            bankBin: "970422",
            bankAccountNo: "0123456789",
            bankAccountName: "KOMAFORGE STUDIO",
            workspaceId: chapter.Series.WorkspaceId,
            seriesId: chapter.SeriesId,
            chapterId: chapter.Id,
            language: language
        );
        transaction.Approve(command.UserId, DateTime.UtcNow);
        _db.PaymentTransactions.Add(transaction);

        var entitlement = ChapterEntitlement.Create(
            userId: command.UserId,
            chapterId: chapter.Id,
            seriesId: chapter.SeriesId,
            workspaceId: chapter.Series.WorkspaceId,
            pricePaidVnd: priceVnd,
            transactionId: transaction.Id,
            language: language
        );
        _db.ChapterEntitlements.Add(entitlement);

        await _db.SaveChangesAsync(ct);

        var remainingBalance = (int)wallet.BalanceVnd;
        return Result<UnlockChapterResponse>.Success(new UnlockChapterResponse(
            ChapterId: chapter.Id.ToString(),
            Success: true,
            RemainingCoins: remainingBalance / 100,
            RemainingBalanceVnd: remainingBalance,
            Message: $"Đã trừ {priceVnd:N0}đ trong Ví. Mở khóa vĩnh viễn thành công!",
            IsPermanent: true
        ));
    }
}
