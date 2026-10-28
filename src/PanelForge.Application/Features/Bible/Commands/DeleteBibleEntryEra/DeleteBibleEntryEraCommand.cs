using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Bible.Commands.DeleteBibleEntryEra;

/// <summary>
/// Xóa toàn bộ một thời kỳ (Era/Stage) của mục Bible (chỉ cho phép nếu không có revision nào thuộc era bị khóa Canon và không chứa bản gốc đầu tiên).
/// </summary>
public sealed record DeleteBibleEntryEraCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    int ChapterNumber,
    string? StageName,
    Guid UserId
) : IRequest<Result<bool>>;

public sealed class DeleteBibleEntryEraCommandHandler : IRequestHandler<DeleteBibleEntryEraCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public DeleteBibleEntryEraCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(DeleteBibleEntryEraCommand cmd, CancellationToken ct)
    {
        var entry = await _dbContext.BibleEntries
            .Include(e => e.Revisions)
            .Include(e => e.SeriesBible)
            .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBible.SeriesId == cmd.SeriesId, ct);

        if (entry is null)
            return Result<bool>.Failure("Mục Bible không tồn tại.", ResultErrorCodes.NotFound);

        // Lấy tất cả revision thuộc Era này
        var eraRevisions = entry.Revisions
            .Where(r => r.EffectiveFromChapterNumber == cmd.ChapterNumber &&
                        (string.IsNullOrEmpty(cmd.StageName) ? string.IsNullOrEmpty(r.StageName) : r.StageName == cmd.StageName))
            .ToList();

        if (eraRevisions.Count == 0)
            return Result<bool>.Failure("Thời kỳ không tồn tại hoặc không có phiên bản nào.", ResultErrorCodes.NotFound);

        if (eraRevisions.Any(r => r.IsLocked))
            return Result<bool>.Failure("Không thể xóa thời kỳ vì đã có phiên bản được khóa Canon.", ResultErrorCodes.Conflict);

        if (eraRevisions.Any(r => r.IsInitialVersion))
            return Result<bool>.Failure("Không thể xóa thời kỳ chứa phiên bản gốc ban đầu của tác phẩm.", ResultErrorCodes.Conflict);

        foreach (var rev in eraRevisions)
        {
            entry.RemoveRevision(rev);
            _dbContext.BibleEntryRevisions.Remove(rev);
        }

        await _dbContext.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}

