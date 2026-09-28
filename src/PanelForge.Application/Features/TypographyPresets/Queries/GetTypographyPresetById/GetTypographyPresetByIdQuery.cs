using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.TypographyPresets.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.TypographyPresets.Queries.GetTypographyPresetById;

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
