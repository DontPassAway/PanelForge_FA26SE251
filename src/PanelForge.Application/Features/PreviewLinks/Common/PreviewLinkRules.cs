using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Preview;

namespace PanelForge.Application.Features.PreviewLinks.Common;

/// <summary>Token của link xem thử: sinh ngẫu nhiên, chỉ lưu SHA-256; kiểm và ghi log mọi lần dùng (BR-09, BR-17).</summary>
public static class PreviewLinkRules
{
    public const int DefaultHours = 72;
    public const int MaxHours = 720;
    public static readonly TimeSpan ImageLinkLifetime = TimeSpan.FromMinutes(10);

    /// <summary>32 byte ngẫu nhiên, base64url (an toàn trong URL).</summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))).ToLowerInvariant();

    /// <summary>
    /// Tìm link theo token và kiểm hạn / thu hồi; luôn thêm một dòng log (người gọi SaveChanges).
    /// Trả link hợp lệ hoặc null kèm lý do (InvalidToken | Expired | Revoked).
    /// </summary>
    public static async Task<(PreviewLink? Link, string? Deny)> ResolveAsync(
        IPanelForgeDbContext db, string? token, string action, string? ip, CancellationToken ct)
    {
        PreviewLink? link = null;
        if (!string.IsNullOrWhiteSpace(token))
        {
            var hash = Hash(token.Trim());
            link = await db.PreviewLinks.FirstOrDefaultAsync(l => l.TokenHash == hash, ct);
        }

        var deny = link is null ? "InvalidToken" : link.DenyReason(DateTime.UtcNow);
        db.PreviewLinkAccessLogs.Add(PreviewLinkAccessLog.Create(link?.Id, action, deny is null, deny, ip));
        return (deny is null ? link : null, deny);
    }

    /// <summary>Thông báo cho người nhận.</summary>
    public static string Message(string? deny) => deny switch
    {
        "Expired" => "Link xem thử đã hết hạn.",
        "Revoked" => "Link xem thử đã bị thu hồi.",
        _ => "Link xem thử không hợp lệ."
    };
}
