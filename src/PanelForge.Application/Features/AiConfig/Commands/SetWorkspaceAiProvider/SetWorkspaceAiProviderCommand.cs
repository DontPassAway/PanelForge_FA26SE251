using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.AiConfig.Commands.SetWorkspaceAiProvider;

/// <summary>Admin chọn API AI cho Workspace; null = bỏ chọn (Studio không gọi được AI chữ cho tới khi chọn lại). Quota/bật-tắt giữ nguyên.</summary>
public sealed record SetWorkspaceAiProviderCommand(Guid WorkspaceId, Guid? ProviderCredentialId) : IRequest<Result<bool>>;

public sealed class SetWorkspaceAiProviderCommandHandler(IPanelForgeDbContext db)
    : IRequestHandler<SetWorkspaceAiProviderCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetWorkspaceAiProviderCommand command, CancellationToken ct)
    {
        if (!await db.StudioWorkspaces.AnyAsync(w => w.Id == command.WorkspaceId, ct))
            return Result<bool>.Failure($"Workspace {command.WorkspaceId} không tồn tại.", ResultErrorCodes.NotFound);

        if (command.ProviderCredentialId is { } id
            && !await db.AiProviderCredentials.AnyAsync(c => c.Id == id && c.IsEnabled, ct))
            return Result<bool>.Failure("API AI không tồn tại hoặc đang tắt.", ResultErrorCodes.Validation);

        var config = await db.WorkspaceAiConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == command.WorkspaceId, ct);
        if (config is null)
        {
            config = WorkspaceAiConfig.Create(command.WorkspaceId);
            db.WorkspaceAiConfigs.Add(config);
        }
        config.UseProviderCredential(command.ProviderCredentialId);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
