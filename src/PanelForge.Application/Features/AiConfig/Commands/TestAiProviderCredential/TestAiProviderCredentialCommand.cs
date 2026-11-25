using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiConfig.Common;

namespace PanelForge.Application.Features.AiConfig.Commands.TestAiProviderCredential;

/// <summary>Nút "Kiểm tra" của Admin: gọi thử provider bằng key đã lưu, không đổi dữ liệu.</summary>
public sealed record TestAiProviderCredentialCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class TestAiProviderCredentialCommandHandler(
    IPanelForgeDbContext db, IAiKeyProtector protector, IAiProviderValidator validator)
    : IRequestHandler<TestAiProviderCredentialCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(TestAiProviderCredentialCommand command, CancellationToken ct)
    {
        var credential = await db.AiProviderCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.Id == command.Id, ct);
        if (credential is null)
            return Result<bool>.Failure("Không tìm thấy API AI này.", ResultErrorCodes.NotFound);

        var key = AiCredentials.Unprotect(protector, credential.ApiKeyEncrypted);
        if (key is null)
            return Result<bool>.Failure("Key đã lưu không giải mã được, hãy nhập lại API key.", ResultErrorCodes.Validation);

        var check = await validator.ProbeAsync(credential.ProviderType, credential.AccountId, key, ct);
        return check.IsSuccess ? check : Result<bool>.Failure(check.ErrorMessage!, ResultErrorCodes.Validation);
    }
}
