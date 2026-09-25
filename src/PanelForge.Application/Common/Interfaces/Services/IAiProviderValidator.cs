using PanelForge.Application.Common;

namespace PanelForge.Application.Interfaces;

/// <summary>
/// Service kiểm tra tính hợp lệ của API Key khi lưu cấu hình AI Provider (UC-02 Alternate Flow).
/// </summary>
public interface IAiProviderValidator
{
    Task<Result<bool>> ValidateCredentialsAsync(string provider, string apiKey, CancellationToken cancellationToken = default);
}
