using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.TypographyPresets.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.TypographyPresets.Queries.GetTypographyPresets;

public sealed record GetTypographyPresetsQuery(Guid SeriesId)
    : IRequest<Result<IReadOnlyList<TypographyPresetDto>>>;

public sealed class GetTypographyPresetsQueryHandler
    : IRequestHandler<GetTypographyPresetsQuery, Result<IReadOnlyList<TypographyPresetDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetTypographyPresetsQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<TypographyPresetDto>>> Handle(
        GetTypographyPresetsQuery query, CancellationToken ct)
    {
        var presets = await _dbContext.TypographyPresets
            .Where(p => p.SeriesId == query.SeriesId)
            .OrderBy(p => p.UsageType).ThenBy(p => p.Name)
            .ToListAsync(ct);

        return Result<IReadOnlyList<TypographyPresetDto>>.Success(
            presets.Select(p => p.ToDto()).ToList());
    }
}
