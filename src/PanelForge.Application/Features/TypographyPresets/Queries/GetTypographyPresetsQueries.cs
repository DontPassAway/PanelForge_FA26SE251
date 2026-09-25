using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.TypographyPresets.Commands;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.TypographyPresets.Queries;

// ─── Get List ─────────────────────────────────────────────────────────────────

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

// ─── Get By Id ────────────────────────────────────────────────────────────────

public sealed record GetTypographyPresetByIdQuery(Guid PresetId, Guid SeriesId)
    : IRequest<Result<TypographyPresetDto>>;

public sealed class GetTypographyPresetByIdQueryHandler
    : IRequestHandler<GetTypographyPresetByIdQuery, Result<TypographyPresetDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetTypographyPresetByIdQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<TypographyPresetDto>> Handle(
        GetTypographyPresetByIdQuery query, CancellationToken ct)
    {
        var preset = await _dbContext.TypographyPresets
            .FirstOrDefaultAsync(p => p.Id == query.PresetId && p.SeriesId == query.SeriesId, ct);

        if (preset is null)
            return Result<TypographyPresetDto>.Failure("TypographyPreset không tồn tại.");

        return Result<TypographyPresetDto>.Success(preset.ToDto());
    }
}
