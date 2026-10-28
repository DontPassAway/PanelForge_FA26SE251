using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Bible.Commands.DeleteBibleEntryRevision;

/// <summary>
/// Xóa một bản sửa đổi (revision) của mục Bible (chỉ cho phép nếu chưa bị khóa Canon và không phải là bản gốc đầu tiên).
/// </summary>
public sealed record DeleteBibleEntryRevisionCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId,
    Guid UserId
) : IRequest<Result<bool>>;

public sealed class DeleteBibleEntryRevisionCommandHandler : IRequestHandler<DeleteBibleEntryRevisionCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public DeleteBibleEntryRevisionCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(DeleteBibleEntryRevisionCommand cmd, CancellationToken ct)
    {
        var entry = await _dbContext.BibleEntries
            .Include(e => e.Revisions)
            .Include(e => e.SeriesBible)
            .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBible.SeriesId == cmd.SeriesId, ct);

        if (entry is null)
            return Result<bool>.Failure("Mục Bible không tồn tại.", ResultErrorCodes.NotFound);

        var revision = entry.Revisions.FirstOrDefault(r => r.Id == cmd.RevisionId);
        if (revision is null)
            return Result<bool>.Failure("Revision không tồn tại.", ResultErrorCodes.NotFound);

        if (revision.IsLocked)
            return Result<bool>.Failure("Không thể xóa revision đã được khóa Canon.", ResultErrorCodes.Conflict);

        if (revision.IsInitialVersion)
            return Result<bool>.Failure("Không thể xóa phiên bản gốc ban đầu (Initial Version).", ResultErrorCodes.Conflict);

        entry.RemoveRevision(revision);
        _dbContext.BibleEntryRevisions.Remove(revision);

        await _dbContext.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}

