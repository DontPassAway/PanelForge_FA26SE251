using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiConfig.Common;
using PanelForge.Application.Features.AiConfig.Models;

namespace PanelForge.Application.Features.AiConfig.Queries.GetAiProviderCredentials;

/// <summary>Danh sách API AI Admin đã khai báo (UC-02 mở rộng), kèm số Workspace đang dùng mỗi API.</summary>
public sealed record GetAiProviderCredentialsQuery : IRequest<Result<IReadOnlyList<AiProviderCredentialDto>>>;

public sealed class GetAiProviderCredentialsQueryHandler(IPanelForgeDbContext db, IAiKeyProtector protector)
    : IRequestHandler<GetAiProviderCredentialsQuery, Result<IReadOnlyList<AiProviderCredentialDto>>>
{
    public async Task<Result<IReadOnlyList<AiProviderCredentialDto>>> Handle(GetAiProviderCredentialsQuery request, CancellationToken ct)
    {
        var credentials = await db.AiProviderCredentials.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        // Một Studio dùng API cho cả chữ lẫn ảnh chỉ tính một lần
        var links = await db.WorkspaceAiConfigs.AsNoTracking()
            .Where(w => w.ProviderCredentialId != null || w.ImageProviderCredentialId != null)
            .Select(w => new { w.WorkspaceId, w.ProviderCredentialId, w.ImageProviderCredentialId })
            .ToListAsync(ct);
        var usage = links
            .SelectMany(w => new[] { w.ProviderCredentialId, w.ImageProviderCredentialId }.OfType<Guid>().Distinct()
                .Select(id => (id, w.WorkspaceId)))
            .GroupBy(x => x.id)
            .ToDictionary(g => g.Key, g => g.Count());

        return Result<IReadOnlyList<AiProviderCredentialDto>>.Success(
            credentials.Select(c => AiCredentials.ToDto(c, protector, usage.GetValueOrDefault(c.Id))).ToList());
    }
}
