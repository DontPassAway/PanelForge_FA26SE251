using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.MasterData;

namespace PanelForge.Application.Features.MasterData.ExportPresets.Commands.DeactivateExportPreset;

public sealed record DeactivateExportPresetCommand(Guid Id) : IRequest<Result<string>>;

public sealed class DeactivateExportPresetCommandHandler
    : IRequestHandler<DeactivateExportPresetCommand, Result<string>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public DeactivateExportPresetCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<string>> Handle(DeactivateExportPresetCommand cmd, CancellationToken ct)
    {
        var entity = await _dbContext.ExportPresets.FirstOrDefaultAsync(e => e.Id == cmd.Id, ct);
        if (entity is null) return Result<string>.Failure("ExportPreset không tồn tại.");
        entity.Deactivate();
        await _dbContext.SaveChangesAsync(ct);
        return Result<string>.Success($"ExportPreset '{entity.Code}' đã được vô hiệu hóa.");
    }
}
