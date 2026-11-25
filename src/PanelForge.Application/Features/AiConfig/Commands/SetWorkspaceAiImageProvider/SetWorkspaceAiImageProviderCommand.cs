using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.AiConfig.Commands.SetWorkspaceAiImageProvider;

/// <summary>Admin chọn API riêng cho sinh ảnh của Workspace (phải có model ảnh); null = sinh ảnh theo API chữ như trước.</summary>
public sealed record SetWorkspaceAiImageProviderCommand(Guid WorkspaceId, Guid? ProviderCredentialId) : IRequest<Result<bool>>;

public sealed class SetWorkspaceAiImageProviderCommandHandler(IPanelForgeDbContext db)
    : IRequestHandler<SetWorkspaceAiImageProviderCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetWorkspaceAiImageProviderCommand command, CancellationToken ct)
    {
        if (!await db.StudioWorkspaces.AnyAsync(w => w.Id == command.WorkspaceId, ct))
            return Result<bool>.Failure($"Workspace {command.WorkspaceId} không tồn tại.", ResultErrorCodes.NotFound);

        if (command.ProviderCredentialId is { } id)
        {
            var credential = await db.AiProviderCredentials.AsNoTracking()
                .Where(c => c.Id == id && c.IsEnabled).Select(c => new { c.ImageModel }).FirstOrDefaultAsync(ct);
            if (credential is null)
                return Result<bool>.Failure("API AI không tồn tại hoặc đang tắt.", ResultErrorCodes.Validation);
            if (credential.ImageModel is null)
                return Result<bool>.Failure("API này chưa khai báo model sinh ảnh. Sửa API và nhập model ảnh trước.", ResultErrorCodes.Validation);
        }

        var config = await db.WorkspaceAiConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == command.WorkspaceId, ct);
        if (config is null)
        {
            config = WorkspaceAiConfig.Create(command.WorkspaceId);
            db.WorkspaceAiConfigs.Add(config);
        }
        config.UseImageProviderCredential(command.ProviderCredentialId);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
