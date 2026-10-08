using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Series.Commands.SetSeriesStoryArc;

/// <summary>V1.2: Producer/Writer ghi story arc của Series (input của Writer Agent). Chỉ đổi story arc, không đụng field khác.</summary>
public sealed record SetSeriesStoryArcCommand(Guid SeriesId, Guid UserId, string? StoryArc) : IRequest<Result<string?>>;

public sealed class SetSeriesStoryArcCommandHandler : IRequestHandler<SetSeriesStoryArcCommand, Result<string?>>
{
    private readonly IPanelForgeDbContext _db;

    public SetSeriesStoryArcCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<string?>> Handle(SetSeriesStoryArcCommand cmd, CancellationToken ct)
    {
        var series = await _db.Series.FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null)
            return Result<string?>.Failure($"Series {cmd.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        series.SetStoryArc(cmd.StoryArc);
        series.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<string?>.Success(series.StoryArc);
    }
}
