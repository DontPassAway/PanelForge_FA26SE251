using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.MasterData.ExportPresets.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.MasterData.ExportPresets.Common;

namespace PanelForge.Application.Features.MasterData.ExportPresets.Queries.GetExportPresets;

public sealed record GetExportPresetsQuery() : IRequest<Result<IReadOnlyList<ExportPresetDto>>>;

public sealed class GetExportPresetsQueryHandler
    : IRequestHandler<GetExportPresetsQuery, Result<IReadOnlyList<ExportPresetDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetExportPresetsQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<ExportPresetDto>>> Handle(GetExportPresetsQuery q, CancellationToken ct)
    {
        var list = await _dbContext.ExportPresets.OrderBy(e => e.Code).ToListAsync(ct);
        return Result<IReadOnlyList<ExportPresetDto>>.Success(list.Select(e => e.ToDto()).ToList());
    }
}
