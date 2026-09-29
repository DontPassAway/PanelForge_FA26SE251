using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.MasterData.ExportPresets.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.MasterData.ExportPresets.Common;

namespace PanelForge.Application.Features.MasterData.ExportPresets.Queries.GetExportPresetById;

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
