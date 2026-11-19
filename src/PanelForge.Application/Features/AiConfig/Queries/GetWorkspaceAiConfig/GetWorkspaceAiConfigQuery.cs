using PanelForge.Application.Common.Interfaces.Services;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiConfig.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.AiConfig.Queries.GetWorkspaceAiConfig;

public sealed record GetWorkspaceAiConfigQuery(Guid WorkspaceId) : IRequest<Result<WorkspaceAiConfigDto>>;

public sealed class GetWorkspaceAiConfigQueryHandler : IRequestHandler<GetWorkspaceAiConfigQuery, Result<WorkspaceAiConfigDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IAiKeyProtector _keyProtector;

    public GetWorkspaceAiConfigQueryHandler(IPanelForgeDbContext dbContext, IAiKeyProtector keyProtector)
    {
        _dbContext = dbContext;
        _keyProtector = keyProtector;
    }

    public async Task<Result<WorkspaceAiConfigDto>> Handle(GetWorkspaceAiConfigQuery request, CancellationToken cancellationToken)
    {
        var config = await _dbContext.WorkspaceAiConfigs
            .FirstOrDefaultAsync(c => c.WorkspaceId == request.WorkspaceId, cancellationToken);

        if (config == null)
        {
            // Tự động khởi tạo cấu hình mặc định cho Workspace nếu chưa có
            config = WorkspaceAiConfig.Create(request.WorkspaceId);
            _dbContext.WorkspaceAiConfigs.Add(config);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        List<string> models;
        try
        {
            models = JsonSerializer.Deserialize<List<string>>(config.AllowedModelsJson) ?? [];
        }
        catch
        {
            models = [.. WorkspaceAiConfig.DefaultAllowedModels];
        }

        return Result<WorkspaceAiConfigDto>.Success(new WorkspaceAiConfigDto(
            Id: config.Id,
            WorkspaceId: config.WorkspaceId,
            Provider: config.Provider,
            HasApiKey: !string.IsNullOrWhiteSpace(config.ApiKeyEncrypted),
            IsEnabled: config.IsEnabled,
            MonthlyTokenQuota: config.MonthlyTokenQuota,
            UsedTokensCurrentMonth: config.UsedTokensCurrentMonth,
            AllowedModels: models,
            LastResetAt: config.LastResetAt,
            CreatedAt: config.CreatedAt,
            UpdatedAt: config.UpdatedAt,
            MonthlyImageQuota: config.MonthlyImageQuota,
            UsedImagesCurrentMonth: config.UsedImagesCurrentMonth,
            ApiKeyUsable: !string.IsNullOrEmpty(_keyProtector.Unprotect(config.ApiKeyEncrypted ?? string.Empty))
        ));
    }
}
