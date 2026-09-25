namespace PanelForge.Application.Interfaces;

/// <summary>
/// Service bảo vệ/mã hóa API Key của AI Provider trước khi lưu vào DB (UC-02, NFR-03).
/// </summary>
public interface IAiKeyProtector
{
    string Protect(string rawKey);
    string Unprotect(string protectedKey);
}
