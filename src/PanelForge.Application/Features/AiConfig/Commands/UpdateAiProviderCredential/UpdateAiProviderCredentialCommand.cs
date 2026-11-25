using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiConfig.Common;
using PanelForge.Application.Features.AiConfig.Models;

namespace PanelForge.Application.Features.AiConfig.Commands.UpdateAiProviderCredential;

/// <summary>Sửa API AI. Đổi key hoặc Account ID thì gọi thử lại trước khi lưu.</summary>
public sealed record UpdateAiProviderCredentialCommand(Guid Id, UpdateAiProviderCredentialRequest Request)
    : IRequest<Result<AiProviderCredentialDto>>;

public sealed class UpdateAiProviderCredentialCommandHandler(
    IPanelForgeDbContext db, IAiKeyProtector protector, IAiProviderValidator validator)
    : IRequestHandler<UpdateAiProviderCredentialCommand, Result<AiProviderCredentialDto>>
{
    public async Task<Result<AiProviderCredentialDto>> Handle(UpdateAiProviderCredentialCommand command, CancellationToken ct)
    {
        var credential = await db.AiProviderCredentials.FirstOrDefaultAsync(c => c.Id == command.Id, ct);
        if (credential is null)
            return Result<AiProviderCredentialDto>.Failure("Không tìm thấy API AI này.", ResultErrorCodes.NotFound);

        var r = command.Request;
        if (AiCredentials.Validate(r.Name, credential.ProviderType, r.AccountId, r.TextModel, r.ImageModel) is { } error)
            return Result<AiProviderCredentialDto>.Failure(error, ResultErrorCodes.Validation);

        var newKey = string.IsNullOrWhiteSpace(r.ApiKey) ? null : r.ApiKey.Trim();
        var accountChanged = !string.Equals(credential.AccountId, r.AccountId?.Trim() is { Length: > 0 } a ? a : null, StringComparison.Ordinal);
        if (newKey is not null || accountChanged)
        {
            var key = newKey ?? AiCredentials.Unprotect(protector, credential.ApiKeyEncrypted);
            if (key is null)
                return Result<AiProviderCredentialDto>.Failure("Key cũ không giải mã được, hãy nhập lại API key.", ResultErrorCodes.Validation);
            var check = await AiCredentials.CheckKeyAsync(validator, credential.ProviderType, r.AccountId, key, ct);
            if (!check.IsSuccess)
                return Result<AiProviderCredentialDto>.Failure(check.ErrorMessage!, ResultErrorCodes.Validation);
        }

        credential.Update(r.Name, r.AccountId, newKey is null ? null : protector.Protect(newKey), r.TextModel, r.ImageModel, r.IsEnabled);
        await db.SaveChangesAsync(ct);

        var count = await db.WorkspaceAiConfigs.CountAsync(w => w.ProviderCredentialId == credential.Id || w.ImageProviderCredentialId == credential.Id, ct);
        return Result<AiProviderCredentialDto>.Success(AiCredentials.ToDto(credential, protector, count));
    }
}
