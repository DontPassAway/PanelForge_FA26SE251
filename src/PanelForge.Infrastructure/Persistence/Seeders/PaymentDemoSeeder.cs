using Microsoft.EntityFrameworkCore;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Persistence.Seeders;

/// <summary>
/// Seed dữ liệu mẫu cho hệ thống thanh toán VietQR và mở khóa chương (Development).
/// KomaForge Studio / Bộ truyện "Mặc Kiếm Lục: Trảm Long Nhai" / Giao dịch mẫu VietQR.
/// </summary>
public static class PaymentDemoSeeder
{
    public static readonly Guid KomaWorkspaceId = Guid.Parse("b1000000-0000-0000-0000-000000000001");
    public static readonly Guid MacKiemLucSeriesId = Guid.Parse("b1000000-0000-0000-0000-000000000002");
    public static readonly Guid Chapter1Id = Guid.Parse("b1000000-0000-0000-0000-000000000003");
    public static readonly Guid Chapter2Id = Guid.Parse("b1000000-0000-0000-0000-000000000004");
    public static readonly Guid TxApprovedId = Guid.Parse("b1000000-0000-0000-0000-000000000005");
    public static readonly Guid TxPendingId = Guid.Parse("b1000000-0000-0000-0000-000000000006");
    public static readonly Guid WalletId = Guid.Parse("b1000000-0000-0000-0000-000000000007");
    public static readonly Guid EntitlementId = Guid.Parse("b1000000-0000-0000-0000-000000000008");

    private static readonly Guid ProducerUser = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ReaderUser = Guid.Parse("88888888-8888-8888-8888-888888888888");

    public static async Task<bool> SeedAsync(PanelForgeDbContext db, CancellationToken ct = default)
    {
        if (await db.StudioWorkspaces.IgnoreQueryFilters().AnyAsync(w => w.Id == KomaWorkspaceId, ct))
            return false;

        var existingUsers = await db.Users.Where(u => u.Id == ProducerUser || u.Id == ReaderUser).Select(u => u.Id).ToListAsync(ct);
        if (!existingUsers.Contains(ProducerUser) || !existingUsers.Contains(ReaderUser))
            return false;

        // 1. Studio Workspace KomaForge Studio với thông tin VietQR ngân hàng MB Bank
        var workspace = StudioWorkspace.Create("KomaForge Studio", ProducerUser);
        SetId(workspace, KomaWorkspaceId);
        workspace.UpdateBankInfo(
            bankBin: "970422",
            bankName: "MB Bank",
            bankAccountNo: "0123456789",
            bankAccountName: "KOMAFORGE STUDIO"
        );
        db.StudioWorkspaces.Add(workspace);
        db.WorkspaceMembers.Add(WorkspaceMember.Create(KomaWorkspaceId, ProducerUser, WorkspaceRole.Producer));

        // 2. Series "Mặc Kiếm Lục: Trảm Long Nhai"
        var series = Series.Create(
            KomaWorkspaceId,
            "Mặc Kiếm Lục: Trảm Long Nhai",
            ReadingDirection.RightToLeft,
            "Thiếu niên mang theo thanh kiếm rỉ sét bước vào Long Nhai, mở ra bí mật ngàn năm của Kiếm Tông.",
            "Kiếm Hiệp / Action",
            "Manga"
        );
        SetId(series, MacKiemLucSeriesId);
        db.Series.Add(series);
        db.SeriesBibles.Add(SeriesBible.Create(MacKiemLucSeriesId, KomaWorkspaceId, "Series Bible", "Series Bible mặc định"));

        // 3. Chapters (Chương 1 Free, Chương 2 Locked giá 10,000 VND)
        var ch1 = Chapter.Create(MacKiemLucSeriesId, 1, "Kiếm Khởi Long Nhai");
        SetId(ch1, Chapter1Id);
        ch1.SetPricing(isLocked: false, priceVnd: 0m);
        ch1.Approve();
        ch1.Publish(DateTime.UtcNow.AddDays(-10));
        db.Chapters.Add(ch1);

        var ch2 = Chapter.Create(MacKiemLucSeriesId, 2, "Huyết Chiến Hắc Long");
        SetId(ch2, Chapter2Id);
        ch2.SetPricing(isLocked: true, priceVnd: 10000m);
        ch2.Approve();
        ch2.Publish(DateTime.UtcNow.AddDays(-5));
        db.Chapters.Add(ch2);

        // 4. Ví độc giả của Frank Reader
        if (!await db.ReaderWallets.IgnoreQueryFilters().AnyAsync(w => w.UserId == ReaderUser, ct))
        {
            var wallet = ReaderWallet.Create(ReaderUser, balanceVnd: 50000m, isVip: false);
            SetId(wallet, WalletId);
            db.ReaderWallets.Add(wallet);
        }

        // 5. Giao dịch thanh toán VietQR mẫu
        // Giao dịch 1: Đã duyệt (Approved) - mở khóa Chương 2
        var txApproved = PaymentTransaction.Create(
            userId: ReaderUser,
            orderCode: "PF882910",
            type: PaymentTransactionType.ChapterUnlock,
            amountVnd: 10000m,
            qrImageUrl: "https://img.vietqr.io/image/970422-0123456789-compact2.png?amount=10000&addInfo=PF882910&accountName=KOMAFORGE%20STUDIO",
            bankBin: "970422",
            bankAccountNo: "0123456789",
            bankAccountName: "KOMAFORGE STUDIO",
            workspaceId: KomaWorkspaceId,
            seriesId: MacKiemLucSeriesId,
            chapterId: Chapter2Id
        );
        SetId(txApproved, TxApprovedId);
        txApproved.Approve(ProducerUser, DateTime.UtcNow.AddDays(-1));
        db.PaymentTransactions.Add(txApproved);

        // Giao dịch 2: Đang chờ chuyển khoản/duyệt (Pending)
        var txPending = PaymentTransaction.Create(
            userId: ReaderUser,
            orderCode: "PF993812",
            type: PaymentTransactionType.Topup,
            amountVnd: 50000m,
            qrImageUrl: "https://img.vietqr.io/image/970422-0123456789-compact2.png?amount=50000&addInfo=PF993812&accountName=KOMAFORGE%20STUDIO",
            bankBin: "970422",
            bankAccountNo: "0123456789",
            bankAccountName: "KOMAFORGE STUDIO",
            workspaceId: KomaWorkspaceId
        );
        SetId(txPending, TxPendingId);
        db.PaymentTransactions.Add(txPending);

        // 6. Quyền mở khóa chương tương ứng với giao dịch Approved
        var entitlement = ChapterEntitlement.Create(
            userId: ReaderUser,
            chapterId: Chapter2Id,
            seriesId: MacKiemLucSeriesId,
            workspaceId: KomaWorkspaceId,
            pricePaidVnd: 10000m,
            transactionId: TxApprovedId,
            unlockedAt: DateTime.UtcNow.AddDays(-1)
        );
        SetId(entitlement, EntitlementId);
        db.ChapterEntitlements.Add(entitlement);

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void SetId(BaseEntity entity, Guid id) =>
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);
}

