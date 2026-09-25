using Microsoft.AspNetCore.DataProtection;
using PanelForge.Application.Interfaces;

namespace PanelForge.Infrastructure.Services;

/// <summary>
/// Sử dụng ASP.NET Core Data Protection để mã hóa và giải mã API Key (UC-02, NFR-03).
/// </summary>
public class AiKeyProtector : IAiKeyProtector
{
    private readonly IDataProtector _protector;

    public AiKeyProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("PanelForge.AiKeyProtector.v1");
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

        try
        {
            return _protector.Unprotect(protectedKey.Trim());
        }
        catch
        {
            // Trả về chuỗi rỗng nếu key không thể giải mã (ví dụ thay đổi key ring)
            return string.Empty;
        }
    }
}
