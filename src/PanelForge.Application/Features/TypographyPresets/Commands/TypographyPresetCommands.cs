using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.TypographyPresets.Commands;

// ─── Create ───────────────────────────────────────────────────────────────────

public sealed record CreateTypographyPresetCommand(
    Guid SeriesId,
    Guid RequestingUserId,
    string Name,
    string FontFamily,
    int FontSize,
    int FontWeight = 400,
    string FontStyle = "Normal",
    decimal LineHeight = 1.2m,
    decimal LetterSpacing = 0m,
    string TextAlign = "Left",
    string UsageType = "Custom",
    bool IsDefault = false
) : IRequest<Result<TypographyPresetDto>>;

public sealed class CreateTypographyPresetCommandHandler
    : IRequestHandler<CreateTypographyPresetCommand, Result<TypographyPresetDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public CreateTypographyPresetCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<TypographyPresetDto>> Handle(
        CreateTypographyPresetCommand cmd, CancellationToken ct)
    {
        // Verify Series exists & user is member of its workspace
        var series = await _dbContext.Series
            .FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null) return Result<TypographyPresetDto>.Failure("Series không tồn tại.");

        var isMember = await _dbContext.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == series.WorkspaceId && m.UserId == cmd.RequestingUserId, ct);
        if (!isMember) return Result<TypographyPresetDto>.Failure("Bạn không có quyền truy cập Series này.");

        TypographyPreset preset;
        try
        {
            preset = TypographyPreset.Create(
                cmd.SeriesId, cmd.Name, cmd.FontFamily, cmd.FontSize,
                cmd.FontWeight, cmd.FontStyle, cmd.LineHeight, cmd.LetterSpacing,
                cmd.TextAlign, cmd.UsageType, cmd.IsDefault);
        }
        catch (ArgumentException ex)
        {
            return Result<TypographyPresetDto>.Failure(ex.Message);
        }

        _dbContext.TypographyPresets.Add(preset);
        await _dbContext.SaveChangesAsync(ct);
        return Result<TypographyPresetDto>.Success(preset.ToDto());
    }
}

// ─── Update ───────────────────────────────────────────────────────────────────

public sealed record UpdateTypographyPresetCommand(
    Guid PresetId,
    Guid SeriesId,
    Guid RequestingUserId,
    string Name,
    string FontFamily,
    int FontSize,
    int FontWeight = 400,
    string FontStyle = "Normal",
    decimal LineHeight = 1.2m,
    decimal LetterSpacing = 0m,
    string TextAlign = "Left",
    string UsageType = "Custom",
    bool IsDefault = false
) : IRequest<Result<TypographyPresetDto>>;

public sealed class UpdateTypographyPresetCommandHandler
    : IRequestHandler<UpdateTypographyPresetCommand, Result<TypographyPresetDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public UpdateTypographyPresetCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<TypographyPresetDto>> Handle(
        UpdateTypographyPresetCommand cmd, CancellationToken ct)
    {
        var series = await _dbContext.Series.FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null) return Result<TypographyPresetDto>.Failure("Series không tồn tại.");

        var isMember = await _dbContext.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == series.WorkspaceId && m.UserId == cmd.RequestingUserId, ct);
        if (!isMember) return Result<TypographyPresetDto>.Failure("Bạn không có quyền truy cập Series này.");

        var preset = await _dbContext.TypographyPresets
            .FirstOrDefaultAsync(p => p.Id == cmd.PresetId && p.SeriesId == cmd.SeriesId, ct);
        if (preset is null) return Result<TypographyPresetDto>.Failure("TypographyPreset không tồn tại.");

        try
        {
            preset.Update(cmd.Name, cmd.FontFamily, cmd.FontSize, cmd.FontWeight,
                cmd.FontStyle, cmd.LineHeight, cmd.LetterSpacing, cmd.TextAlign, cmd.UsageType, cmd.IsDefault);
        }
        catch (ArgumentException ex)
        {
            return Result<TypographyPresetDto>.Failure(ex.Message);
        }

        await _dbContext.SaveChangesAsync(ct);
        return Result<TypographyPresetDto>.Success(preset.ToDto());
    }
}

// ─── Delete ───────────────────────────────────────────────────────────────────

public sealed record DeleteTypographyPresetCommand(
    Guid PresetId, Guid SeriesId, Guid RequestingUserId
) : IRequest<Result<string>>;

public sealed class DeleteTypographyPresetCommandHandler
    : IRequestHandler<DeleteTypographyPresetCommand, Result<string>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public DeleteTypographyPresetCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<string>> Handle(DeleteTypographyPresetCommand cmd, CancellationToken ct)
    {
        var series = await _dbContext.Series.FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null) return Result<string>.Failure("Series không tồn tại.");

        var isMember = await _dbContext.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == series.WorkspaceId && m.UserId == cmd.RequestingUserId, ct);
        if (!isMember) return Result<string>.Failure("Bạn không có quyền truy cập Series này.");

        var preset = await _dbContext.TypographyPresets
            .FirstOrDefaultAsync(p => p.Id == cmd.PresetId && p.SeriesId == cmd.SeriesId, ct);
        if (preset is null) return Result<string>.Failure("TypographyPreset không tồn tại.");

        _dbContext.TypographyPresets.Remove(preset);
        await _dbContext.SaveChangesAsync(ct);
        return Result<string>.Success("TypographyPreset đã được xóa.");
    }
}
