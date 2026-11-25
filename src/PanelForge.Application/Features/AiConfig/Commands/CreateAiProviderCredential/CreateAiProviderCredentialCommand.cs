using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiConfig.Common;
using PanelForge.Application.Features.AiConfig.Models;
using PanelForge.Domain.Entities.Ai;

namespace PanelForge.Application.Features.AiConfig.Commands.CreateAiProviderCredential;

/// <summary>Admin thêm một API AI (Gemini/Cloudflare). Key được gọi thử rồi mã hoá trước khi lưu (UC-02, NFR-03).</summary>
public sealed record CreateAiProviderCredentialCommand(CreateAiProviderCredentialRequest Request)
    : IRequest<Result<AiProviderCredentialDto>>;

public sealed class CreateAiProviderCredentialCommandHandler(
    IPanelForgeDbContext db, IAiKeyProtector protector, IAiProviderValidator validator)
    : IRequestHandler<CreateAiProviderCredentialCommand, Result<AiProviderCredentialDto>>
{
    public async Task<Result<AiProviderCredentialDto>> Handle(CreateAiProviderCredentialCommand command, CancellationToken ct)
    {
        var r = command.Request;
        if (AiCredentials.Validate(r.Name, r.ProviderType, r.AccountId, r.TextModel, r.ImageModel) is { } error)
            return Result<AiProviderCredentialDto>.Failure(error, ResultErrorCodes.Validation);
        if (AiCredentials.ValidateApiKey(r.ApiKey) is { } keyError)
            return Result<AiProviderCredentialDto>.Failure(keyError, ResultErrorCodes.Validation);

        var check = await AiCredentials.CheckKeyAsync(validator, r.ProviderType, r.AccountId, r.ApiKey, ct);
        if (!check.IsSuccess)
            return Result<AiProviderCredentialDto>.Failure(check.ErrorMessage!, ResultErrorCodes.Validation);

        var credential = AiProviderCredential.Create(r.Name, r.ProviderType, r.AccountId, protector.Protect(r.ApiKey.Trim()),
            r.TextModel, r.ImageModel);
        db.AiProviderCredentials.Add(credential);
        await db.SaveChangesAsync(ct);
        return Result<AiProviderCredentialDto>.Success(AiCredentials.ToDto(credential, protector, 0));
    }
}
