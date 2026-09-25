using PanelForge.Application.Common;
using PanelForge.Application.Interfaces;

namespace PanelForge.Infrastructure.Services;

/// <summary>
/// Xác thực credentials của AI Provider khi Administrator lưu cấu hình (UC-02 Alternate Flow).
/// Nếu sai thông tin xác thực, trả về lỗi và từ chối lưu.
/// Phiên bản dev: kiểm tra định dạng và cấu trúc API key tương ứng từng Provider.
/// </summary>
public class AiProviderValidator : IAiProviderValidator
{
    public async Task<Result<bool>> ValidateCredentialsAsync(
        string provider,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result<bool>.Failure("API Key không được để trống.");
        }

        var trimmedKey = apiKey.Trim();
        var trimmedProvider = provider?.Trim() ?? string.Empty;

        // 1. Kiểm tra đối với Google Gemini
        if (trimmedProvider.Contains("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmedKey.Length < 10)
            {
                return Result<bool>.Failure("API Key của Google Gemini không hợp lệ (độ dài quá ngắn hoặc sai định dạng).");
            }
        }
        // 2. Kiểm tra đối với OpenAI
        else if (trimmedProvider.Contains("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            if (!trimmedKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) || trimmedKey.Length < 15)
            {
                return Result<bool>.Failure("API Key của OpenAI không hợp lệ (phải bắt đầu bằng 'sk-' và có độ dài hợp lệ).");
            }
        }
        // 3. Kiểm tra đối với Claude / Anthropic
        else if (trimmedProvider.Contains("Anthropic", StringComparison.OrdinalIgnoreCase) ||
                 trimmedProvider.Contains("Claude", StringComparison.OrdinalIgnoreCase))
        {
            if (!trimmedKey.StartsWith("sk-ant-", StringComparison.OrdinalIgnoreCase) || trimmedKey.Length < 15)
            {
                return Result<bool>.Failure("API Key của Anthropic/Claude không hợp lệ (phải bắt đầu bằng 'sk-ant-').");
            }
        }
        else
        {
            // Provider chung
            if (trimmedKey.Length < 8)
            {
                return Result<bool>.Failure($"API Key không hợp lệ cho nhà cung cấp '{trimmedProvider}'.");
            }
        }

        return Result<bool>.Success(true);
    }
}
