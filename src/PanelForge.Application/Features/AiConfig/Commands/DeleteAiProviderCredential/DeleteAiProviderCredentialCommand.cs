using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.AiConfig.Commands.DeleteAiProviderCredential;

/// <summary>Xoá mềm API AI. Còn Workspace đang dùng thì từ chối (gỡ khỏi Workspace hoặc tắt IsEnabled trước).</summary>
public sealed record DeleteAiProviderCredentialCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteAiProviderCredentialCommandHandler(IPanelForgeDbContext db)
    : IRequestHandler<DeleteAiProviderCredentialCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteAiProviderCredentialCommand command, CancellationToken ct)
    {
        var credential = await db.AiProviderCredentials.FirstOrDefaultAsync(c => c.Id == command.Id, ct);
        if (credential is null)
            return Result<bool>.Failure("Không tìm thấy API AI này.", ResultErrorCodes.NotFound);

        var inUse = await db.WorkspaceAiConfigs.CountAsync(w => w.ProviderCredentialId == command.Id || w.ImageProviderCredentialId == command.Id, ct);
        if (inUse > 0)
            return Result<bool>.Failure($"Còn {inUse} Workspace đang dùng API này. Đổi API cho các Workspace đó trước khi xoá.",
                ResultErrorCodes.Conflict);

        db.AiProviderCredentials.Remove(credential); // SaveChanges chuyển thành soft-delete
        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
