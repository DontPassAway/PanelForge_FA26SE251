using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Pages.Models;

namespace PanelForge.Application.Features.Pages.Queries.GetKeepOutZones;

public sealed record GetKeepOutZonesQuery(Guid PageId) : IRequest<Result<PageKeepOutZonesDto>>;

public sealed class GetKeepOutZonesQueryHandler : IRequestHandler<GetKeepOutZonesQuery, Result<PageKeepOutZonesDto>>
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IPanelForgeDbContext _db;

    public GetKeepOutZonesQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<PageKeepOutZonesDto>> Handle(GetKeepOutZonesQuery q, CancellationToken ct)
    {
        if (!await _db.Pages.AsNoTracking().AnyAsync(p => p.Id == q.PageId, ct))
            return Result<PageKeepOutZonesDto>.Failure($"Page {q.PageId} không tồn tại.", ResultErrorCodes.NotFound);

        var row = await _db.PageKeepOutZones.AsNoTracking().FirstOrDefaultAsync(z => z.PageId == q.PageId, ct);
        var zones = row is null ? [] : JsonSerializer.Deserialize<List<KeepOutZoneDto>>(row.ZonesJson, Json) ?? [];
        return Result<PageKeepOutZonesDto>.Success(new PageKeepOutZonesDto(q.PageId, zones, row?.Version ?? 0, row?.UpdatedAt));
    }
}
