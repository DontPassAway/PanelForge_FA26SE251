using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Application.Features.Bible.Commands.CancelReplacementRevision;

public sealed record CancelReplacementRevisionCommand(
    Guid SeriesId,
    Guid? BibleEntryId,
    Guid RevisionId,
    Guid UserId
) : IRequest<Result<bool>>;

public sealed class CancelReplacementRevisionCommandHandler : IRequestHandler<CancelReplacementRevisionCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public CancelReplacementRevisionCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(CancelReplacementRevisionCommand command, CancellationToken cancellationToken)
    {
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == command.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        BibleEntry? entry = null;
        if (bibleId != null)
        {
            entry = command.BibleEntryId.HasValue
                ? await _dbContext.BibleEntries
                    .Include(e => e.Revisions).ThenInclude(r => r.Events)
                    .FirstOrDefaultAsync(e => e.Id == command.BibleEntryId.Value && e.SeriesBibleId == bibleId.Value, cancellationToken)
                : await _dbContext.BibleEntries
                    .Include(e => e.Revisions).ThenInclude(r => r.Events)
                    .FirstOrDefaultAsync(e => e.SeriesBibleId == bibleId.Value && e.Revisions.Any(r => r.Id == command.RevisionId), cancellationToken);
        }

        if (entry == null)
        {
            return Result<bool>.Failure(
                command.BibleEntryId.HasValue
                    ? $"Mục BibleEntry {command.BibleEntryId} không tồn tại."
                    : $"Revision {command.RevisionId} không tồn tại.",
                ResultErrorCodes.NotFound);
        }

        var revision = entry.Revisions.FirstOrDefault(r => r.Id == command.RevisionId);
        if (revision == null)
        {
            return Result<bool>.Failure($"Revision {command.RevisionId} không tồn tại.", ResultErrorCodes.NotFound);
        }

        if (revision.SupersedesRevisionId == null)
        {
            return Result<bool>.Failure("Chỉ có thể huỷ bản thay thế (bản có supersedes_revision_id).", ResultErrorCodes.Conflict);
        }

        if (revision.IsLocked)
        {
            return Result<bool>.Failure("Không thể huỷ bản thay thế đã được khoá Canon.", ResultErrorCodes.Conflict);
        }

        if (revision.HasReviewHistory)
        {
            return Result<bool>.Failure("Bản thay thế đã có sự kiện kiểm duyệt, không thể huỷ (append-only).", ResultErrorCodes.Conflict);
        }

        // Q23: binding đang trỏ bản đó về bản kế thừa / bản cũ bị thay thế
        var affectedBindings = await _dbContext.ChapterBibleBindings
            .Where(b => b.BibleEntryId == entry.Id && b.BoundRevisionId == revision.Id)
            .ToListAsync(cancellationToken);

        foreach (var binding in affectedBindings)
        {
            binding.UpdateBinding(
                boundRevisionId: revision.SupersedesRevisionId,
                source: binding.Source,
                chosenByWriter: false,
                boundById: command.UserId
            );
        }

        entry.RemoveRevision(revision);
        _dbContext.BibleEntryRevisions.Remove(revision);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
