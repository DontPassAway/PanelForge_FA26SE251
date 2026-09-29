using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.TypographyPresets.Commands.DeleteTypographyPreset;

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
