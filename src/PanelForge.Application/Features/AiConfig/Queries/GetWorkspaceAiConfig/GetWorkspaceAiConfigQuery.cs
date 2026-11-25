using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiConfig.Common;
using PanelForge.Application.Features.AiConfig.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.AiConfig.Queries.GetWorkspaceAiConfig;

public sealed record GetWorkspaceAiConfigQuery(Guid WorkspaceId) : IRequest<Result<WorkspaceAiConfigDto>>;

public sealed class GetWorkspaceAiConfigQueryHandler(IPanelForgeDbContext db)
    : IRequestHandler<GetWorkspaceAiConfigQuery, Result<WorkspaceAiConfigDto>>
{
    public async Task<Result<WorkspaceAiConfigDto>> Handle(GetWorkspaceAiConfigQuery request, CancellationToken ct)
    {
        var config = await db.WorkspaceAiConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == request.WorkspaceId, ct);

        if (config == null)
        {
            // Tự động khởi tạo cấu hình mặc định cho Workspace nếu chưa có
            config = WorkspaceAiConfig.Create(request.WorkspaceId);
            db.WorkspaceAiConfigs.Add(config);
            await db.SaveChangesAsync(ct);
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

        return Result<WorkspaceAiConfigDto>.Success(await ToDtoAsync(db, config, models, ct));
    }

    /// <summary>
    /// TASK-350/355: HasApiKey = có API chữ dùng được (Studio tự chọn hoặc API mặc định); ApiKeyUsable = API đó còn bật.
    /// Provider = loại của API đó (rỗng nếu không có). ProviderCredentialId vẫn là lựa chọn riêng của Studio (null = theo mặc định).
    /// </summary>
    internal static async Task<WorkspaceAiConfigDto> ToDtoAsync(
        IPanelForgeDbContext db, WorkspaceAiConfig config, List<string> models, CancellationToken ct)
    {
        var cred = await AiCredentials.EffectiveTextIdAsync(db, config, ct) is { } id
            ? await db.AiProviderCredentials.AsNoTracking()
                .Where(c => c.Id == id).Select(c => new { c.ProviderType, c.IsEnabled }).FirstOrDefaultAsync(ct)
            : null;

        return new WorkspaceAiConfigDto(
            Id: config.Id,
            WorkspaceId: config.WorkspaceId,
            Provider: cred?.ProviderType ?? "",
            HasApiKey: cred is not null,
            IsEnabled: config.IsEnabled,
            MonthlyTokenQuota: config.MonthlyTokenQuota,
            UsedTokensCurrentMonth: config.UsedTokensCurrentMonth,
            AllowedModels: models,
            LastResetAt: config.LastResetAt,
            CreatedAt: config.CreatedAt,
            UpdatedAt: config.UpdatedAt,
            MonthlyImageQuota: config.MonthlyImageQuota,
            UsedImagesCurrentMonth: config.UsedImagesCurrentMonth,
            ApiKeyUsable: cred?.IsEnabled == true,
            ProviderCredentialId: config.ProviderCredentialId,
            ImageProviderCredentialId: config.ImageProviderCredentialId);
    }
}
