using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.UnlockBibleEntryRevision;

/// <summary>
/// Mở khoá một revision của character sheet.
/// </summary>
public sealed record UnlockBibleEntryRevisionCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId,
    Guid UserId
) : IRequest<Result<BibleEntryRevisionDto>>;

public sealed class UnlockBibleEntryRevisionCommandHandler
    : IRequestHandler<UnlockBibleEntryRevisionCommand, Result<BibleEntryRevisionDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public UnlockBibleEntryRevisionCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<BibleEntryRevisionDto>> Handle(UnlockBibleEntryRevisionCommand command, CancellationToken cancellationToken)
    {
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == command.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var entry = bibleId == null
            ? null
            : await _dbContext.BibleEntries
                .Include(e => e.Revisions)
                .FirstOrDefaultAsync(e => e.Id == command.BibleEntryId && e.SeriesBibleId == bibleId.Value, cancellationToken);

        var revision = entry?.Revisions.FirstOrDefault(r => r.Id == command.RevisionId);
        if (entry == null || revision == null)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Revision {command.RevisionId} của mục Bible {command.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);
        }

        if (entry.Category != BibleEntryCategory.Character)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                "Chỉ thao tác được trên character sheet (mục thuộc nhóm Nhân vật).", ResultErrorCodes.Validation);
        }

        if (!revision.IsLocked)
        {
            return Result<BibleEntryRevisionDto>.Failure("Revision này chưa được khoá.", ResultErrorCodes.Conflict);
        }

        revision.Unlock();
        entry.UpdatedBy = command.UserId.ToString();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<BibleEntryRevisionDto>.Success(new BibleEntryRevisionDto(
            Id: revision.Id,
            BibleEntryId: revision.BibleEntryId,
            VersionNumber: revision.VersionNumber,
            Summary: revision.Summary,
            SnapshotJson: revision.SnapshotJson,
            ContentHash: revision.ContentHash,
            AuthorId: revision.AuthorId,
            AssociatedChapterId: revision.AssociatedChapterId,
            EffectiveFromChapterNumber: revision.EffectiveFromChapterNumber,
            IsInitialVersion: revision.IsInitialVersion,
            CreatedAt: revision.CreatedAt,
            IsLocked: revision.IsLocked,
            LockedAt: revision.LockedAt,
            LockedById: revision.LockedById));
    }
}
