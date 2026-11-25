namespace PanelForge.Application.Features.AiConfig.Models;

/// <summary>Không bao giờ trả key: chỉ KeyHint dạng "••••abcd".</summary>
public sealed record AiProviderCredentialDto(
    Guid Id,
    string Name,
    string ProviderType,
    string? AccountId,
    string KeyHint,
    string TextModel,
    string? ImageModel,
    bool IsEnabled,
    int WorkspaceCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>ProviderType: "Gemini" | "Cloudflare" (Cloudflare bắt buộc AccountId).</summary>
public sealed record CreateAiProviderCredentialRequest(
    string Name, string ProviderType, string? AccountId, string ApiKey, string TextModel, string? ImageModel);

/// <summary>ApiKey trống = giữ key cũ. ProviderType không đổi được (tạo credential mới).</summary>
public sealed record UpdateAiProviderCredentialRequest(
    string Name, string? AccountId, string? ApiKey, string TextModel, string? ImageModel, bool IsEnabled);

/// <summary>null = Workspace quay về key Gemini riêng trong ai-config.</summary>
public sealed record SetWorkspaceAiProviderRequest(Guid? ProviderCredentialId);
