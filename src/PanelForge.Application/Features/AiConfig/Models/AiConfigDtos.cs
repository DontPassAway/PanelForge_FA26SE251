namespace PanelForge.Application.Features.AiConfig.Models;

public sealed record WorkspaceAiConfigDto(
    Guid Id,
    Guid WorkspaceId,
    string Provider,
    bool HasApiKey,
    bool IsEnabled,
    long MonthlyTokenQuota,
    long UsedTokensCurrentMonth,
    IReadOnlyList<string> AllowedModels,
    DateTime? LastResetAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    long MonthlyImageQuota = 0,
    long UsedImagesCurrentMonth = 0,
    bool ApiKeyUsable = false,
    Guid? ProviderCredentialId = null,
    Guid? ImageProviderCredentialId = null
);

public sealed record UpdateWorkspaceAiConfigRequest(
    bool IsEnabled,
    long MonthlyTokenQuota,
    IReadOnlyList<string> AllowedModels,
    long? MonthlyImageQuota = null
);
