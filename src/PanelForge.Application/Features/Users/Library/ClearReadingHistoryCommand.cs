using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Users.Library;

/// <summary>Xoá lịch sử đọc của chính người gọi (một bộ khi có <see cref="SeriesId"/>, ngược lại tất cả).</summary>
public sealed record ClearReadingHistoryCommand(Guid UserId, Guid? SeriesId = null) : IRequest<Result<int>>;

public sealed class ClearReadingHistoryCommandHandler : IRequestHandler<ClearReadingHistoryCommand, Result<int>>
{
    private readonly IPanelForgeDbContext _db;

    public ClearReadingHistoryCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<int>> Handle(ClearReadingHistoryCommand cmd, CancellationToken ct)
    {
        var rows = await _db.ReaderReadingProgress
            .Where(p => p.UserId == cmd.UserId && (cmd.SeriesId == null || p.SeriesId == cmd.SeriesId))
            .ToListAsync(ct);
        _db.ReaderReadingProgress.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
        return Result<int>.Success(rows.Count);
    }
}
