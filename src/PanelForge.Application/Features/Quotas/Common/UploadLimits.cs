using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.Quotas.Common;

/// <summary>Loại file tải lên, mỗi loại một giới hạn kích thước.</summary>
public enum UploadKind
{
    /// <summary>Artwork trang/panel, ảnh trang gửi đi tách panel.</summary>
    Artwork,
    /// <summary>Ảnh tham chiếu Bible.</summary>
    ReferenceImage,
    /// <summary>Bìa Series/Chương/Studio, avatar và banner Series.</summary>
    CoverImage
}

/// <summary>
/// Giới hạn kích thước file đang dùng: bản ghi <c>upload_limit_policies</c> mới nhất, chưa có thì mặc định trong
/// <see cref="UploadLimitPolicy"/>. Mọi luồng nhận file của người dùng gọi <see cref="CheckAsync"/> trước khi giữ chỗ dung lượng.
/// </summary>
public static class UploadLimits
{
    public static async Task<UploadLimitsDto> GetEffectiveAsync(IPanelForgeDbContext db, CancellationToken ct)
    {
        var latest = await db.UploadLimitPolicies.AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.ArtworkMaxBytes,
                p.ReferenceImageMaxBytes,
                p.CoverImageMaxBytes,
                p.CreatedAt,
                UpdatedByName = db.Users.Where(u => u.Id == p.ChangedByUserId).Select(u => u.FullName).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        return latest is null
            ? new UploadLimitsDto(UploadLimitPolicy.DefaultArtworkMaxBytes, UploadLimitPolicy.DefaultReferenceImageMaxBytes,
                UploadLimitPolicy.DefaultCoverImageMaxBytes, UploadLimitPolicy.ArtworkCeilingBytes, UploadLimitPolicy.ImageCeilingBytes,
                true, null, null)
            : new UploadLimitsDto(latest.ArtworkMaxBytes, latest.ReferenceImageMaxBytes, latest.CoverImageMaxBytes,
                UploadLimitPolicy.ArtworkCeilingBytes, UploadLimitPolicy.ImageCeilingBytes, false, latest.CreatedAt, latest.UpdatedByName);
    }

    public static long MaxBytes(UploadLimitsDto limits, UploadKind kind) => kind switch
    {
        UploadKind.Artwork => limits.ArtworkMaxBytes,
        UploadKind.ReferenceImage => limits.ReferenceImageMaxBytes,
        _ => limits.CoverImageMaxBytes
    };

    /// <summary>
    /// Thông báo lỗi (dùng với mã <see cref="ResultErrorCodes.FileTooLarge"/>) nếu file vượt giới hạn của loại đó, null nếu hợp lệ.
    /// File rỗng do từng luồng tự báo.
    /// FE dựa vào mã lỗi và <c>GET /api/assets/upload-limits</c> để báo đúng mức theo ngôn ngữ đang dùng.
    /// </summary>
    public static async Task<string?> CheckAsync(IPanelForgeDbContext db, UploadKind kind, long length, CancellationToken ct)
    {
        var max = MaxBytes(await GetEffectiveAsync(db, ct), kind);
        return length > max ? $"File vượt giới hạn {FormatMb(max)} MB cho loại này." : null;
    }

    public static string FormatMb(long bytes)
        => (bytes / (double)UploadLimitPolicy.MiB).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
