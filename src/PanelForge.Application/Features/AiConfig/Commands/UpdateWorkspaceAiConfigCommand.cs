using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiConfig.Models;
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

    public UpdateWorkspaceAiConfigCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<WorkspaceAiConfigDto>> Handle(UpdateWorkspaceAiConfigCommand request, CancellationToken cancellationToken)
    {
        var config = await _dbContext.WorkspaceAiConfigs
            .FirstOrDefaultAsync(c => c.WorkspaceId == request.WorkspaceId, cancellationToken);

        var modelsJson = JsonSerializer.Serialize(request.AllowedModels);

        if (config == null)
        {
            config = WorkspaceAiConfig.Create(
                request.WorkspaceId,
                request.Provider,
                request.ApiKey,
                request.MonthlyTokenQuota,
                modelsJson
            );
            _dbContext.WorkspaceAiConfigs.Add(config);
        }
        else
        {
            config.UpdateSettings(
                request.Provider,
                request.ApiKey,
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
            AllowedModels: request.AllowedModels,
            LastResetAt: config.LastResetAt,
            CreatedAt: config.CreatedAt,
            UpdatedAt: config.UpdatedAt
        ));
    }
}
