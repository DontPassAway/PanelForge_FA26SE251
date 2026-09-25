using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPresets.Models;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.SeriesPresets.Queries;

public sealed record GetSeriesPresetQuery(Guid SeriesId) : IRequest<Result<SeriesPresetDto>>;

public sealed class GetSeriesPresetQueryHandler : IRequestHandler<GetSeriesPresetQuery, Result<SeriesPresetDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetSeriesPresetQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SeriesPresetDto>> Handle(GetSeriesPresetQuery request, CancellationToken cancellationToken)
    {
        var preset = await _dbContext.SeriesPresets
            .FirstOrDefaultAsync(p => p.SeriesId == request.SeriesId, cancellationToken);

        if (preset == null)
        {
            // Tự động khởi tạo preset mặc định cho Series nếu chưa có
            preset = SeriesPreset.Create(request.SeriesId);
            _dbContext.SeriesPresets.Add(preset);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

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
