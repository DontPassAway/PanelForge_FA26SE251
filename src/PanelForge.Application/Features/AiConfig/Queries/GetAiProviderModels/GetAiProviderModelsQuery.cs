using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiConfig.Common;
using PanelForge.Application.Features.AiConfig.Models;
using PanelForge.Domain.Entities.Ai;

namespace PanelForge.Application.Features.AiConfig.Queries.GetAiProviderModels;

/// <summary>Model chữ/ảnh mà API đã lưu dùng được, lấy bằng token đã lưu (token không ra khỏi Backend). Hiện chỉ Cloudflare.</summary>
public sealed record GetAiProviderModelsQuery(Guid Id) : IRequest<Result<IReadOnlyList<AiProviderModelDto>>>;

public sealed class GetAiProviderModelsQueryHandler(IPanelForgeDbContext db, IAiKeyProtector protector, IAiProviderValidator provider)
    : IRequestHandler<GetAiProviderModelsQuery, Result<IReadOnlyList<AiProviderModelDto>>>
{
    public async Task<Result<IReadOnlyList<AiProviderModelDto>>> Handle(GetAiProviderModelsQuery query, CancellationToken ct)
    {
        var credential = await db.AiProviderCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.Id == query.Id, ct);
        if (credential is null)
            return Result<IReadOnlyList<AiProviderModelDto>>.Failure("Không tìm thấy API AI này.", ResultErrorCodes.NotFound);
        if (credential.ProviderType != AiProviderCredential.Cloudflare || string.IsNullOrWhiteSpace(credential.AccountId))
            return Result<IReadOnlyList<AiProviderModelDto>>.Failure(
                "Hiện chỉ lấy được danh sách model của Cloudflare; với Gemini hãy nhập tên model.", ResultErrorCodes.Validation);

        var key = AiCredentials.Unprotect(protector, credential.ApiKeyEncrypted);
        if (key is null)
            return Result<IReadOnlyList<AiProviderModelDto>>.Failure("Key đã lưu không giải mã được, hãy nhập lại API key.", ResultErrorCodes.Validation);

        var models = await provider.ListCloudflareModelsAsync(credential.AccountId, key, ct);
        return models.IsSuccess ? models : Result<IReadOnlyList<AiProviderModelDto>>.Failure(models.ErrorMessage!, ResultErrorCodes.Validation);
    }
}
