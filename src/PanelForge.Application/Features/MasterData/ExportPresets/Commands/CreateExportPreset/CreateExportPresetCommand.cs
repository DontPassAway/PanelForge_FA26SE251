using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Application.Features.MasterData.ExportPresets.Models;
using PanelForge.Application.Features.MasterData.ExportPresets.Common;

namespace PanelForge.Application.Features.MasterData.ExportPresets.Commands.CreateExportPreset;

public sealed record CreateExportPresetCommand(
    string Code, string Name, string FormatName, string? ConfigOptionsJson
) : IRequest<Result<ExportPresetDto>>;

public sealed class CreateExportPresetCommandHandler
    : IRequestHandler<CreateExportPresetCommand, Result<ExportPresetDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public CreateExportPresetCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<ExportPresetDto>> Handle(CreateExportPresetCommand cmd, CancellationToken ct)
    {
        var code = cmd.Code.Trim().ToUpperInvariant();
        if (await _dbContext.ExportPresets.AnyAsync(e => e.Code == code, ct))
            return Result<ExportPresetDto>.Failure($"ExportPreset với Code '{code}' đã tồn tại.");

        ExportPreset entity;
        try { entity = ExportPreset.Create(cmd.Code, cmd.Name, cmd.FormatName, cmd.ConfigOptionsJson); }
        catch (ArgumentException ex) { return Result<ExportPresetDto>.Failure(ex.Message); }

        _dbContext.ExportPresets.Add(entity);
        await _dbContext.SaveChangesAsync(ct);
        return Result<ExportPresetDto>.Success(entity.ToDto());
    }
}
