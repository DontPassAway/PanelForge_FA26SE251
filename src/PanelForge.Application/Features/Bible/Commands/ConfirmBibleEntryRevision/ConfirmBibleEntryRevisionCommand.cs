using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.ConfirmBibleEntryRevision;

/// <summary>
/// V2.6 (Q20): Editor xác nhận = khoá Canon. Chỉ áp dụng từ ConceptSubmitted,
/// Editor phải khác Artist đã nộp Concept (BR-06).
/// </summary>
public sealed record ConfirmBibleEntryRevisionCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId,
    Guid UserId,
    Guid? ConceptEventId = null
) : IRequest<Result<BibleEntryRevisionDto>>;

public sealed class ConfirmBibleEntryRevisionCommandHandler
    : IRequestHandler<ConfirmBibleEntryRevisionCommand, Result<BibleEntryRevisionDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public ConfirmBibleEntryRevisionCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BibleEntryRevisionDto>> Handle(ConfirmBibleEntryRevisionCommand cmd, CancellationToken ct)
    {
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == cmd.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        var entry = bibleId == null
            ? null
            : await _dbContext.BibleEntries
                .Include(e => e.Revisions).ThenInclude(r => r.Events)
                .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBibleId == bibleId.Value, ct);

        var revision = entry?.Revisions.FirstOrDefault(r => r.Id == cmd.RevisionId);
        if (entry == null || revision == null)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Revision {cmd.RevisionId} của mục Bible {cmd.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);
        }

        if (!entry.IsVisualDesign)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                "Chỉ khoá Canon được mục có thiết kế hình (Nhân vật, Địa điểm, Đạo cụ).", ResultErrorCodes.Validation);
        }

        if (revision.IsLocked)
        {
            return Result<BibleEntryRevisionDto>.Failure("Revision này đã được khoá trước đó.", ResultErrorCodes.Conflict);
        }

        if (revision.ReviewStatus != BibleRevisionStatus.ConceptSubmitted)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Chỉ xác nhận được revision đang ở trạng thái ConceptSubmitted, hiện là {revision.ReviewStatus}.", ResultErrorCodes.Conflict);
        }

        var targetConceptEventId = revision.CurrentConceptEventId;
        if (!targetConceptEventId.HasValue)
        {
            return Result<BibleEntryRevisionDto>.Failure("Revision chưa có phiên bản concept nào để xác nhận.", ResultErrorCodes.Conflict);
        }

        if (cmd.ConceptEventId.HasValue && cmd.ConceptEventId.Value != targetConceptEventId.Value)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                "Phiên bản concept xác nhận không phải phiên bản đang nộp mới nhất; Artist vừa nộp bản mới.", ResultErrorCodes.Conflict);
        }

        var currentEvent = revision.Events.FirstOrDefault(e => e.Id == targetConceptEventId.Value);
        if ((currentEvent?.ActorId.HasValue == true && currentEvent.ActorId.Value == cmd.UserId)
            || (revision.ArtistUserId.HasValue && revision.ArtistUserId.Value == cmd.UserId))
        {
            return Result<BibleEntryRevisionDto>.Failure(
                "Bạn đã nộp Concept cho phiên bản này; Canon phải do một Editor khác xác nhận (BR-06).", ResultErrorCodes.Forbidden);
        }

        try
        {
            revision.ConfirmCanon(cmd.UserId, targetConceptEventId.Value);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<BibleEntryRevisionDto>.Failure(ex.Message, ResultErrorCodes.Forbidden);
        }
        catch (InvalidOperationException ex)
        {
            return Result<BibleEntryRevisionDto>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }

        entry.UpdatedBy = cmd.UserId.ToString();
        await _dbContext.SaveChangesAsync(ct);

        return Result<BibleEntryRevisionDto>.Success(BibleEntryRevisionDto.From(revision));
    }
}
