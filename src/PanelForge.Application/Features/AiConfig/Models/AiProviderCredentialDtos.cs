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
    DateTime? UpdatedAt,
    bool IsDefaultText = false,
    bool IsDefaultImage = false);

/// <summary>ProviderType: "Gemini" | "Cloudflare" (Cloudflare bắt buộc AccountId).</summary>
public sealed record CreateAiProviderCredentialRequest(
    string Name, string ProviderType, string? AccountId, string ApiKey, string TextModel, string? ImageModel);

/// <summary>ApiKey trống = giữ key cũ. ProviderType không đổi được (tạo credential mới).</summary>
public sealed record UpdateAiProviderCredentialRequest(
    string Name, string? AccountId, string? ApiKey, string TextModel, string? ImageModel, bool IsEnabled);

/// <summary>
/// Một model của nhà cung cấp. Kind: "text" | "image". Free = model Cloudflare tự chạy (tiền tố @cf/, trừ vào 10.000 Neuron/ngày);
/// false = model đối tác cần credit AI Gateway.
/// </summary>
public sealed record AiProviderModelDto(string Id, string Kind, bool Free, string? Description);

/// <summary>null = bỏ chọn API (Studio không gọi được AI tới khi chọn lại).</summary>
public sealed record SetWorkspaceAiProviderRequest(Guid? ProviderCredentialId);

/// <summary>TASK-355: Kind "text" | "image"; ProviderCredentialId null = bỏ API mặc định loại đó.</summary>
public sealed record SetDefaultAiProviderRequest(string Kind, Guid? ProviderCredentialId);
