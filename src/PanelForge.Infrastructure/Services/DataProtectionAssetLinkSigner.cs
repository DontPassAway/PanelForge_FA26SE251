using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.Services;

/// <summary>Link tải ngắn hạn ký bằng ASP.NET Data Protection (đã đăng ký sẵn): token tự hết hạn và không sửa được.</summary>
public sealed class DataProtectionAssetLinkSigner : IAssetLinkSigner
{
    private readonly ITimeLimitedDataProtector _protector;

    public DataProtectionAssetLinkSigner(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("PanelForge.AssetAccessLink").ToTimeLimitedDataProtector();

    public string Create(Guid assetReferenceId, Guid userId, TimeSpan lifetime) =>
        _protector.Protect($"{assetReferenceId:N}|{userId:N}", DateTimeOffset.UtcNow.Add(lifetime));

    public AssetLinkClaims? Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            var parts = _protector.Unprotect(token).Split('|');
            return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var reference)
                                     && Guid.TryParseExact(parts[1], "N", out var user)
                ? new AssetLinkClaims(reference, user)
                : null;
        }
        catch (CryptographicException)
        {
            return null; // sai chữ ký, bị sửa hoặc hết hạn
        }
    }
}
