using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Application.Features.Assets.Common;

/// <summary>
/// OPS-02: giữ chỗ dung lượng Studio trước khi ghi file lên kho. Phần giữ chỗ nằm trên entity đang track và chỉ thành thật
/// khi người gọi <c>SaveChangesAsync</c> (khoá <c>xmin</c> của Workspace): ghi file lỗi thì không lưu DB, chỗ giữ tự mất.
/// File đã có dưới cùng khoá (khoá theo SHA-256 nội dung, kho idempotent) thì không tính lại.
/// </summary>
internal static class StorageQuota
{
    public const string ExceededMessage = "Vượt quota dung lượng của Studio, không thể tải thêm.";
    public const string ConcurrentMessage = "Studio vừa có upload khác cùng lúc. Tải lại rồi thử lại.";

    public static async Task<bool> TryReserveAsync(
        IPanelForgeDbContext db, IAssetStorage storage, Guid workspaceId, string key, long bytes, CancellationToken ct)
    {
        await using (var existing = await storage.OpenReadAsync(key, ct))
        {
            if (existing is not null) return true;
        }

        var workspace = await db.StudioWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
        return workspace is not null && workspace.TryReserveStorage(bytes);
    }
}
