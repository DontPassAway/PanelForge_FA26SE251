using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.MasterData.ExportPresets.Commands;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.MasterData.ExportPresets.Queries;

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

public sealed record GetExportPresetByIdQuery(Guid Id) : IRequest<Result<ExportPresetDto>>;

public sealed class GetExportPresetByIdQueryHandler
    : IRequestHandler<GetExportPresetByIdQuery, Result<ExportPresetDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public GetExportPresetByIdQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<ExportPresetDto>> Handle(GetExportPresetByIdQuery q, CancellationToken ct)
    {
        var e = await _dbContext.ExportPresets.FirstOrDefaultAsync(x => x.Id == q.Id, ct);
        if (e is null) return Result<ExportPresetDto>.Failure("ExportPreset không tồn tại.");
        return Result<ExportPresetDto>.Success(e.ToDto());
    }
}
