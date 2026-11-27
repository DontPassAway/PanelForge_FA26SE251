using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Ai;

/// <summary>
/// Một API AI do Administrator khai báo (Gemini hoặc Cloudflare Workers AI). Nhiều Workspace có thể dùng chung một credential
/// qua <see cref="Workspaces.WorkspaceAiConfig.ProviderCredentialId"/>. Key luôn lưu dạng đã mã hoá (IAiKeyProtector).
/// </summary>
public class AiProviderCredential : BaseEntity
{
    public const string Gemini = "Gemini";
    public const string Cloudflare = "Cloudflare";

    public string Name { get; private set; } = default!;
    public string ProviderType { get; private set; } = default!;
    /// <summary>Chỉ Cloudflare dùng (Account ID trên dash.cloudflare.com).</summary>
    public string? AccountId { get; private set; }
    public string ApiKeyEncrypted { get; private set; } = default!;
    /// <summary>Model văn bản/vision, vd. gemini-3.8-flash hoặc @cf/meta/llama-3.3-70b-instruct-fp8-fast.</summary>
    public string TextModel { get; private set; } = default!;
    /// <summary>Model sinh ảnh (tuỳ chọn), vd. @cf/black-forest-labs/flux-1-schnell. Null = dùng cấu hình ảnh mặc định của hệ thống.</summary>
    public string? ImageModel { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    /// <summary>TASK-355: API chữ dùng cho Studio chưa tự chọn API. Tối đa một credential (unique index lọc).</summary>
    public bool IsDefaultText { get; private set; }
    /// <summary>TASK-355: API ảnh dùng cho Studio chưa tự chọn API ảnh. Tối đa một credential.</summary>
    public bool IsDefaultImage { get; private set; }

    private AiProviderCredential() { }

    public void SetDefaultText(bool value) { IsDefaultText = value; UpdatedAt = DateTime.UtcNow; }
    public void SetDefaultImage(bool value) { IsDefaultImage = value; UpdatedAt = DateTime.UtcNow; }

    public static bool IsKnownProvider(string? p) => p is Gemini or Cloudflare;

    public static AiProviderCredential Create(string name, string providerType, string? accountId, string apiKeyEncrypted,
        string textModel, string? imageModel) => new()
    {
        Name = name.Trim(),
        ProviderType = providerType,
        AccountId = Clean(accountId),
        ApiKeyEncrypted = apiKeyEncrypted,
        TextModel = textModel.Trim(),
        ImageModel = Clean(imageModel),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    /// <summary><paramref name="apiKeyEncrypted"/> null = giữ key cũ.</summary>
    public void Update(string name, string? accountId, string? apiKeyEncrypted, string textModel, string? imageModel, bool isEnabled)
    {
        Name = name.Trim();
        AccountId = Clean(accountId);
        if (apiKeyEncrypted is not null) ApiKeyEncrypted = apiKeyEncrypted;
        TextModel = textModel.Trim();
        ImageModel = Clean(imageModel);
        IsEnabled = isEnabled;
        UpdatedAt = DateTime.UtcNow;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
