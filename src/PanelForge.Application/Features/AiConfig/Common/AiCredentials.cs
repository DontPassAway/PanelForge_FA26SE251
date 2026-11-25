using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiConfig.Models;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.AiConfig.Common;

/// <summary>Provider/model/key thực sự dùng cho một lần gọi AI. ApiKey đã giải mã: chỉ giữ trong bộ nhớ, không log.</summary>
public sealed record ResolvedAiCredential(
    string Provider, string TextModel, string? ImageModel, string? ApiKey, string? AccountId, bool FromCredential);

/// <summary>
/// Một nơi duy nhất chọn API cho Workspace: chỉ credential Admin gắn (bảng ai_provider_credentials).
/// Studio chưa chọn → API mặc định của Admin (TASK-355). Không có / đã tắt / đã xoá → không có key
/// (TASK-350: bỏ key Gemini riêng theo Studio).
/// </summary>
public static class AiCredentials
{
    /// <summary>Id API chữ thực sự dùng: Studio tự chọn, không thì API mặc định đang bật. Null = không có API.</summary>
    public static async Task<Guid?> EffectiveTextIdAsync(IPanelForgeDbContext db, WorkspaceAiConfig config, CancellationToken ct) =>
        config.ProviderCredentialId
        ?? await db.AiProviderCredentials.AsNoTracking().Where(x => x.IsDefaultText && x.IsEnabled)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);

    public static async Task<ResolvedAiCredential> ResolveAsync(IPanelForgeDbContext db, IAiKeyProtector protector,
        WorkspaceAiConfig config, string defaultTextModel, CancellationToken ct)
    {
        if (await EffectiveTextIdAsync(db, config, ct) is not { } id)
            return new(AiProtocol.ProviderGemini, defaultTextModel, null, null, null, true);

        var c = await db.AiProviderCredentials.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.IsEnabled, ct);
        return c is null
            ? new(AiProtocol.ProviderGemini, defaultTextModel, null, null, null, true)
            : new(c.ProviderType, c.TextModel, c.ImageModel, Unprotect(protector, c.ApiKeyEncrypted), c.AccountId, true);
    }

    /// <summary>
    /// API dùng cho sinh ảnh: API ảnh riêng của Workspace nếu đã gán, không thì API ảnh mặc định (TASK-355),
    /// không có nữa thì giống <see cref="ResolveAsync"/>.
    /// API ảnh đã chọn bị tắt/xoá hoặc không có model ảnh → ImageModel/ApiKey null để handler chặn, không tự rơi về API khác.
    /// </summary>
    public static async Task<ResolvedAiCredential> ResolveImageAsync(IPanelForgeDbContext db, IAiKeyProtector protector,
        WorkspaceAiConfig config, string defaultTextModel, CancellationToken ct)
    {
        var imageId = config.ImageProviderCredentialId
            ?? await db.AiProviderCredentials.AsNoTracking().Where(x => x.IsDefaultImage && x.IsEnabled)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (imageId is not { } id)
            return await ResolveAsync(db, protector, config, defaultTextModel, ct);

        var c = await db.AiProviderCredentials.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.IsEnabled, ct);
        return c is null || c.ImageModel is null
            ? new(c?.ProviderType ?? AiProtocol.ProviderGemini, defaultTextModel, null, null, null, true)
            : new(c.ProviderType, c.TextModel, c.ImageModel, Unprotect(protector, c.ApiKeyEncrypted), c.AccountId, true);
    }

    public static string? Unprotect(IAiKeyProtector protector, string? encrypted) =>
        protector.Unprotect(encrypted ?? string.Empty) is { Length: > 0 } k ? k : null; // giữ đúng cách gọi cũ: luôn hỏi protector

    /// <summary>"••••abcd" để Admin nhận ra key mà không lộ key.</summary>
    public static string KeyHint(string? rawKey) =>
        string.IsNullOrEmpty(rawKey) ? "" : "••••" + rawKey[^Math.Min(4, rawKey.Length)..];

    /// <summary>Lỗi nhập liệu đầu tiên, null nếu hợp lệ.</summary>
    public static string? Validate(string? name, string? providerType, string? accountId, string? textModel, string? imageModel)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) return "Tên không được trống (tối đa 100 ký tự).";
        if (!AiProviderCredential.IsKnownProvider(providerType)) return "ProviderType phải là 'Gemini' hoặc 'Cloudflare'.";
        if (providerType == AiProviderCredential.Cloudflare && string.IsNullOrWhiteSpace(accountId))
            return "Cloudflare cần Account ID (xem trên dash.cloudflare.com).";
        if (accountId?.Trim().Length > 100) return "Account ID tối đa 100 ký tự.";
        if (string.IsNullOrWhiteSpace(textModel) || textModel.Trim().Length > 200) return "Model văn bản không được trống (tối đa 200 ký tự).";
        if (imageModel?.Trim().Length > 200) return "Model ảnh tối đa 200 ký tự.";
        return null;
    }

    /// <summary>Gọi thử provider; 429/5xx không kết luận key sai nên vẫn cho qua (như UC-02).</summary>
    public static Task<Result<bool>> CheckKeyAsync(IAiProviderValidator validator, string providerType, string? accountId,
        string apiKey, CancellationToken ct) =>
        providerType == AiProviderCredential.Cloudflare
            ? validator.ValidateCloudflareAsync(accountId!.Trim(), apiKey.Trim(), ct)
            : validator.ValidateCredentialsAsync(AiProviderCredential.Gemini, apiKey.Trim(), ct);

    public static AiProviderCredentialDto ToDto(AiProviderCredential c, IAiKeyProtector protector, int workspaceCount) =>
        new(c.Id, c.Name, c.ProviderType, c.AccountId, KeyHint(Unprotect(protector, c.ApiKeyEncrypted)), c.TextModel,
            c.ImageModel, c.IsEnabled, workspaceCount, c.CreatedAt, c.UpdatedAt, c.IsDefaultText, c.IsDefaultImage);
}
