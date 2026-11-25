using PanelForge.Application.Common;
using PanelForge.Application.Features.AiConfig.Models;

namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>
/// Service kiểm tra tính hợp lệ của API Key khi lưu cấu hình AI Provider (UC-02 Alternate Flow).
/// </summary>
public interface IAiProviderValidator
{
    Task<Result<bool>> ValidateCredentialsAsync(string provider, string apiKey, CancellationToken cancellationToken = default);

    /// <summary>Kiểm tra API token Cloudflare Workers AI với Account ID.</summary>
    Task<Result<bool>> ValidateCloudflareAsync(string accountId, string apiToken, CancellationToken cancellationToken = default);

    /// <summary>Danh sách model sinh chữ và sinh ảnh của tài khoản Cloudflare (để Admin chọn thay vì gõ tay).</summary>
    Task<Result<IReadOnlyList<AiProviderModelDto>>> ListCloudflareModelsAsync(string accountId, string apiToken, CancellationToken cancellationToken = default);
}
