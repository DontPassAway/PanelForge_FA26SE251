using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.MasterData;

namespace PanelForge.Application.Features.MasterData.ExportPresets.Commands;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record ExportPresetDto(
    Guid Id, string Code, string Name, string FormatName,
    string ConfigOptionsJson, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);

public static class ExportPresetExtensions
{
    public static ExportPresetDto ToDto(this ExportPreset e) => new(
        e.Id, e.Code, e.Name, e.FormatName, e.ConfigOptionsJson, e.IsActive, e.CreatedAt, e.UpdatedAt);
}

public sealed record CreateExportPresetRequest(string Code, string Name, string FormatName, string? ConfigOptionsJson);
public sealed record UpdateExportPresetRequest(string Name, string FormatName, string? ConfigOptionsJson, bool IsActive);

// ─── Create ───────────────────────────────────────────────────────────────────

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

// ─── Update ───────────────────────────────────────────────────────────────────

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

// ─── Deactivate ───────────────────────────────────────────────────────────────

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
