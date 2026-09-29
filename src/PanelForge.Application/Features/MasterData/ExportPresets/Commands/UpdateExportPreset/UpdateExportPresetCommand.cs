using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Application.Features.MasterData.ExportPresets.Models;
using PanelForge.Application.Features.MasterData.ExportPresets.Common;

namespace PanelForge.Application.Features.MasterData.ExportPresets.Commands.UpdateExportPreset;

public sealed record UpdateExportPresetCommand(
    Guid Id, string Name, string FormatName, string? ConfigOptionsJson, bool IsActive
) : IRequest<Result<ExportPresetDto>>;

public sealed class UpdateExportPresetCommandHandler
    : IRequestHandler<UpdateExportPresetCommand, Result<ExportPresetDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public UpdateExportPresetCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<ExportPresetDto>> Handle(UpdateExportPresetCommand cmd, CancellationToken ct)
    {
        var entity = await _dbContext.ExportPresets.FirstOrDefaultAsync(e => e.Id == cmd.Id, ct);
        if (entity is null) return Result<ExportPresetDto>.Failure("ExportPreset không tồn tại.");

        try { entity.Update(cmd.Name, cmd.FormatName, cmd.ConfigOptionsJson, cmd.IsActive); }
        catch (ArgumentException ex) { return Result<ExportPresetDto>.Failure(ex.Message); }

        await _dbContext.SaveChangesAsync(ct);
        return Result<ExportPresetDto>.Success(entity.ToDto());
    }
}
