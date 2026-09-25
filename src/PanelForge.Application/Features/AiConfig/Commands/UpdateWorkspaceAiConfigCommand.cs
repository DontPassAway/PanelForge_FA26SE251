using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiConfig.Models;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Application.Features.AiConfig.Commands;

public sealed record UpdateWorkspaceAiConfigCommand(
    Guid WorkspaceId,
    string Provider,
    string? ApiKey,
    bool IsEnabled,
    long MonthlyTokenQuota,
    IReadOnlyList<string> AllowedModels
) : IRequest<Result<WorkspaceAiConfigDto>>;

public sealed class UpdateWorkspaceAiConfigCommandHandler : IRequestHandler<UpdateWorkspaceAiConfigCommand, Result<WorkspaceAiConfigDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IAiKeyProtector _keyProtector;
    private readonly IAiProviderValidator _providerValidator;

    public UpdateWorkspaceAiConfigCommandHandler(
        IPanelForgeDbContext dbContext,
        IAiKeyProtector keyProtector,
        IAiProviderValidator providerValidator)
    {
        _dbContext = dbContext;
        _keyProtector = keyProtector;
        _providerValidator = providerValidator;
    }

    public async Task<Result<WorkspaceAiConfigDto>> Handle(UpdateWorkspaceAiConfigCommand request, CancellationToken cancellationToken)
    {
        var workspaceExists = await _dbContext.StudioWorkspaces
            .AnyAsync(w => w.Id == request.WorkspaceId, cancellationToken);

        if (!workspaceExists)
        {
            return Result<WorkspaceAiConfigDto>.Failure($"Workspace {request.WorkspaceId} không tồn tại.");
        }

        string? encryptedKey = null;

        // UC-02 Alternate Flow: Kiểm tra credentials khi Administrator nhập/thay đổi API Key
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            var validationResult = await _providerValidator.ValidateCredentialsAsync(
                request.Provider, request.ApiKey, cancellationToken);

            if (!validationResult.IsSuccess)
            {
                return Result<WorkspaceAiConfigDto>.Failure(
                    $"Xác thực thông tin cấu hình AI Provider thất bại: {validationResult.ErrorMessage}");
            }

            // UC-02 / NFR-03: Mã hóa API Key trước khi lưu trữ
            encryptedKey = _keyProtector.Protect(request.ApiKey);
        }

        var config = await _dbContext.WorkspaceAiConfigs
            .FirstOrDefaultAsync(c => c.WorkspaceId == request.WorkspaceId, cancellationToken);

        var allowedModels = request.AllowedModels is { Count: > 0 }
            ? request.AllowedModels
            : (config != null
                ? (JsonSerializer.Deserialize<List<string>>(config.AllowedModelsJson) ?? ["gemini-1.5-flash", "gemini-1.5-pro"])
                : ["gemini-1.5-flash", "gemini-1.5-pro"]);

        var modelsJson = JsonSerializer.Serialize(allowedModels);

        if (config == null)
        {
            config = WorkspaceAiConfig.Create(
                request.WorkspaceId,
                request.Provider,
                encryptedKey,
                request.MonthlyTokenQuota,
                modelsJson
            );
            config.UpdateSettings(
                request.Provider,
                encryptedKey,
                request.IsEnabled,
                request.MonthlyTokenQuota,
                modelsJson
            );
            _dbContext.WorkspaceAiConfigs.Add(config);
        }
        else
        {
            config.UpdateSettings(
                request.Provider,
                encryptedKey,
                request.IsEnabled,
                request.MonthlyTokenQuota,
                modelsJson
            );
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<WorkspaceAiConfigDto>.Success(new WorkspaceAiConfigDto(
            Id: config.Id,
            WorkspaceId: config.WorkspaceId,
            Provider: config.Provider,
            HasApiKey: !string.IsNullOrWhiteSpace(config.ApiKeyEncrypted),
            IsEnabled: config.IsEnabled,
            MonthlyTokenQuota: config.MonthlyTokenQuota,
            UsedTokensCurrentMonth: config.UsedTokensCurrentMonth,
            AllowedModels: allowedModels,
            LastResetAt: config.LastResetAt,
            CreatedAt: config.CreatedAt,
            UpdatedAt: config.UpdatedAt
        ));
    }
}
