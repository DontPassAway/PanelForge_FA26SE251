namespace PanelForge.Application.Features.Admin.Models;

public record AdminWorkspaceDto(
    Guid Id,
    string Name,
    Guid OwnerId,
    string OwnerName,
    string OwnerEmail,
    long StorageQuotaBytes,
    long UsedStorageBytes,
    int MemberCount,
    int SeriesCount,
    DateTime CreatedAt,
    string? BankBin = null,
    string? BankName = null,
    string? BankAccountNo = null,
    string? BankAccountName = null
);

/// <summary>Cấu hình thanh toán cấp hệ thống (đọc từ appsettings/env SePay__*), chỉ để Admin kiểm tra.</summary>
public record AdminPaymentConfigDto(
    bool WebhookApiKeyConfigured,
    string WebhookPath,
    string DefaultBankBin,
    string DefaultBankName,
    string DefaultBankAccountNo,
    string DefaultBankAccountName,
    bool DefaultBankIsPlaceholder
);

public record UpdateWorkspaceQuotaRequest(
    long StorageQuotaBytes
);
