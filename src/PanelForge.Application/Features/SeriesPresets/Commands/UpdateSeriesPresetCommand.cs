using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPresets.Models;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.SeriesPresets.Commands;

public sealed record UpdateSeriesPresetCommand(
    Guid SeriesId,
    string TypographyPresetsJson,
    string ConsistencyRulesJson
) : IRequest<Result<SeriesPresetDto>>;

public sealed class UpdateSeriesPresetCommandHandler : IRequestHandler<UpdateSeriesPresetCommand, Result<SeriesPresetDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UpdateSeriesPresetCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SeriesPresetDto>> Handle(UpdateSeriesPresetCommand request, CancellationToken cancellationToken)
    {
        var preset = await _dbContext.SeriesPresets
            .FirstOrDefaultAsync(p => p.SeriesId == request.SeriesId, cancellationToken);

        if (preset == null)
        {
            preset = SeriesPreset.Create(request.SeriesId, request.TypographyPresetsJson, request.ConsistencyRulesJson);
            _dbContext.SeriesPresets.Add(preset);
        }
        else
        {
            preset.UpdatePresets(request.TypographyPresetsJson, request.ConsistencyRulesJson);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<SeriesPresetDto>.Success(new SeriesPresetDto(
            Id: preset.Id,
            SeriesId: preset.SeriesId,
            TypographyPresetsJson: preset.TypographyPresetsJson,
            ConsistencyRulesJson: preset.ConsistencyRulesJson,
            CreatedAt: preset.CreatedAt,
            UpdatedAt: preset.UpdatedAt
        ));
    }
}
