using System.Security.Cryptography;

namespace PanelForge.Application.Features.Assets.Common;

/// <summary>Luật thuần (không DB) cho upload artwork: hash, loại file, tên file (BR-04).</summary>
internal static class AssetRules
{
    /// <summary>Giới hạn một file artwork. Nội dung đọc hết vào bộ nhớ nên giữ nhỏ (ponytail: chuyển sang stream khi cần file lớn hơn).</summary>
    public const int MaxArtworkBytes = 25 * 1024 * 1024;

    public static string ComputeSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>Chuẩn hoá hash client gửi về 64 hex thường; null nếu sai định dạng.</summary>
    public static string? NormalizeSha256(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        return v is { Length: 64 } && v.All(Uri.IsHexDigit) ? v : null;
    }

    /// <summary>
    /// Nhận dạng PNG / JPEG / WebP theo byte đầu file; null nếu không phải. Không tin Content-Type của client:
    /// loại file do server quyết định.
    /// </summary>
    public static string? DetectImageContentType(byte[] c)
    {
        if (c.Length >= 8 && c[0] == 0x89 && c[1] == 0x50 && c[2] == 0x4E && c[3] == 0x47
            && c[4] == 0x0D && c[5] == 0x0A && c[6] == 0x1A && c[7] == 0x0A)
            return "image/png";

        if (c.Length >= 3 && c[0] == 0xFF && c[1] == 0xD8 && c[2] == 0xFF)
            return "image/jpeg";

        if (c.Length >= 12 && c[0] == 'R' && c[1] == 'I' && c[2] == 'F' && c[3] == 'F'
            && c[8] == 'W' && c[9] == 'E' && c[10] == 'B' && c[11] == 'P')
            return "image/webp";

        return null;
    }

    /// <summary>Chỉ giữ phần tên file (bỏ đường dẫn, ký tự điều khiển), tối đa 255 ký tự; rỗng thì dùng "artwork".</summary>
    public static string SanitizeFileName(string? name)
    {
        var leaf = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        var clean = new string(leaf.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (clean.Length == 0) return "artwork";
        return clean.Length > 255 ? clean[..255] : clean;
    }
}
