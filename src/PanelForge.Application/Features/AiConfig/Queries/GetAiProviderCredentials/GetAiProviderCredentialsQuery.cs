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
        var usage = await db.WorkspaceAiConfigs.AsNoTracking()
            .Where(w => w.ProviderCredentialId != null)
            .GroupBy(w => w.ProviderCredentialId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return Result<IReadOnlyList<AiProviderCredentialDto>>.Success(
            credentials.Select(c => AiCredentials.ToDto(c, protector, usage.GetValueOrDefault(c.Id))).ToList());
    }
}
