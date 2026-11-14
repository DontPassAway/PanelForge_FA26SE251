using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.LockBibleEntryRevision;

/// <summary>
/// V1.2 (Obsolete Q20): Thay thế bằng ConfirmBibleEntryRevisionCommand.
/// </summary>
[Obsolete("Use ConfirmBibleEntryRevisionCommand instead per Q20.")]
public sealed record LockBibleEntryRevisionCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId,
    Guid UserId
) : IRequest<Result<BibleEntryRevisionDto>>;

[Obsolete("Use ConfirmBibleEntryRevisionCommandHandler instead per Q20.")]
public sealed class LockBibleEntryRevisionCommandHandler
    : IRequestHandler<LockBibleEntryRevisionCommand, Result<BibleEntryRevisionDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public LockBibleEntryRevisionCommandHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<BibleEntryRevisionDto>> Handle(LockBibleEntryRevisionCommand command, CancellationToken cancellationToken)
    {
        // Entry phải thuộc đúng Bible của seriesId trên route (chống IDOR, giống AddBibleEntryRevision).
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == command.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var entry = bibleId == null
            ? null
            : await _dbContext.BibleEntries
                .Include(e => e.Revisions).ThenInclude(r => r.Events)
                .FirstOrDefaultAsync(e => e.Id == command.BibleEntryId && e.SeriesBibleId == bibleId.Value, cancellationToken);

        var revision = entry?.Revisions.FirstOrDefault(r => r.Id == command.RevisionId);
        if (entry == null || revision == null)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Revision {command.RevisionId} của mục Bible {command.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);
        }

        if (!entry.IsVisualDesign)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                "Chỉ khoá được mục có thiết kế hình (Nhân vật, Địa điểm, Đạo cụ).", ResultErrorCodes.Validation);
        }

        if (revision.IsLocked)
        {
            return Result<BibleEntryRevisionDto>.Failure("Revision này đã được khoá trước đó.", ResultErrorCodes.Conflict);
        }

        if (revision.ReviewStatus != BibleRevisionStatus.ConceptSubmitted)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Chỉ khoá Canon được revision đã nộp Concept (ConceptSubmitted), hiện là {revision.ReviewStatus}.",
                ResultErrorCodes.Conflict);
        }

        revision.Lock(command.UserId);
        entry.UpdatedBy = command.UserId.ToString();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<BibleEntryRevisionDto>.Success(BibleEntryRevisionDto.From(revision));
    }
}
