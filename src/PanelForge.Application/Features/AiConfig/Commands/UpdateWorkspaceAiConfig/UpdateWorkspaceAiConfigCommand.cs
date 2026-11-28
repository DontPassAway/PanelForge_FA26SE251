using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiConfig.Models;
using PanelForge.Application.Features.AiConfig.Queries.GetWorkspaceAiConfig;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.AiConfig.Commands.UpdateWorkspaceAiConfig;

/// <summary>
/// Bật/tắt, model của Studio. Hạn mức token/ảnh/lượt gọi đặt ở màn Tài nguyên (OPS-23/24); hai trường quota ở đây
/// còn nhận cho client cũ, null = giữ nguyên. API dùng (chữ/ảnh) chọn qua SetWorkspaceAiProvider/SetWorkspaceAiImageProvider;
/// TASK-350: không còn nhận Provider/ApiKey riêng theo Studio.
/// </summary>
public sealed record UpdateWorkspaceAiConfigCommand(
    Guid WorkspaceId,
    bool IsEnabled,
    long? MonthlyTokenQuota,
    IReadOnlyList<string> AllowedModels,
    long? MonthlyImageQuota = null
) : IRequest<Result<WorkspaceAiConfigDto>>;

public sealed class UpdateWorkspaceAiConfigCommandHandler(
    IPanelForgeDbContext db,
    PanelForge.Application.Common.Interfaces.Services.IAiKeyProtector? protector = null)
    : IRequestHandler<UpdateWorkspaceAiConfigCommand, Result<WorkspaceAiConfigDto>>
{
    public async Task<Result<WorkspaceAiConfigDto>> Handle(UpdateWorkspaceAiConfigCommand request, CancellationToken ct)
    {
        if (!await db.StudioWorkspaces.AnyAsync(w => w.Id == request.WorkspaceId, ct))
            return Result<WorkspaceAiConfigDto>.Failure($"Workspace {request.WorkspaceId} không tồn tại.");

        var config = await db.WorkspaceAiConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == request.WorkspaceId, ct);

        var allowedModels = request.AllowedModels is { Count: > 0 }
            ? request.AllowedModels
            : (config != null
                ? (JsonSerializer.Deserialize<List<string>>(config.AllowedModelsJson) ?? [.. WorkspaceAiConfig.DefaultAllowedModels])
                : [.. WorkspaceAiConfig.DefaultAllowedModels]);
        var modelsJson = JsonSerializer.Serialize(allowedModels);

        if (config == null)
        {
            config = WorkspaceAiConfig.Create(request.WorkspaceId);
            db.WorkspaceAiConfigs.Add(config);
        }
        // provider rỗng = giữ nguyên, apiKey null = không đụng cột key cũ
        config.UpdateSettings("", null, request.IsEnabled, request.MonthlyTokenQuota ?? config.MonthlyTokenQuota, modelsJson);

        // V1.2: null = giữ nguyên quota ảnh hiện tại
        if (request.MonthlyImageQuota.HasValue)
            config.SetMonthlyImageQuota(request.MonthlyImageQuota.Value);

        await db.SaveChangesAsync(ct);

        return Result<WorkspaceAiConfigDto>.Success(
            await GetWorkspaceAiConfigQueryHandler.ToDtoAsync(db, config, [.. allowedModels], ct, protector));
    }
}
