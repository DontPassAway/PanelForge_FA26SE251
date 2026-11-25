using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.Services;

/// <summary>
/// Sử dụng ASP.NET Core Data Protection để mã hóa và giải mã API Key (UC-02, NFR-03).
/// </summary>
public class AiKeyProtector : IAiKeyProtector
{
    public const string Purpose = "PanelForge.AiKeyProtector.v1";

    private readonly IDataProtector _protector;
    private readonly IDataProtector? _legacyProtector;
    private readonly ILogger<AiKeyProtector>? _logger;

    public AiKeyProtector(
        IDataProtectionProvider provider,
        ILogger<AiKeyProtector>? logger = null,
        IDataProtector? legacyProtector = null)
    {
        _protector = provider.CreateProtector(Purpose);
        _legacyProtector = legacyProtector;
        _logger = logger;
    }

    public string Protect(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return string.Empty;

        return _protector.Protect(rawKey.Trim());
    }

    public string Unprotect(string protectedKey)
    {
        if (string.IsNullOrWhiteSpace(protectedKey))
            return string.Empty;

        var trimmed = protectedKey.Trim();
        try
        {
            return _protector.Unprotect(trimmed);
        }
        catch (CryptographicException)
        {
            if (_legacyProtector is not null)
            {
                try
                {
                    return _legacyProtector.Unprotect(trimmed);
                }
                catch (CryptographicException)
                {
                    // rơi xuống log cảnh báo bên dưới
                }
            }

            _logger?.LogWarning("Không giải mã được API key AI: khoá Data Protection đã thay đổi hoặc không khớp.");
            return string.Empty;
        }
        catch
        {
            _logger?.LogWarning("Không giải mã được API key AI: định dạng ciphertext không hợp lệ.");
            return string.Empty;
        }
    }
}
