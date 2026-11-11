using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.LockBibleEntryRevision;

/// <summary>
/// V1.2: Producer duyệt và khoá một revision của character sheet (mục Nhân vật) làm tham chiếu.
/// Không có thao tác mở khoá: muốn thay thì thêm revision mới có hiệu lực từ chương khác, hoặc Producer thay thế
/// tại chương đó kèm lý do (AddBibleEntryRevision với SupersedesRevisionId).
/// Quyền Producer do [RequireWorkspaceRole] kiểm tra theo seriesId trên route.
/// </summary>
public sealed record LockBibleEntryRevisionCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId,
    Guid UserId
) : IRequest<Result<BibleEntryRevisionDto>>;

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

        if (entry.Category != BibleEntryCategory.Character)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                "Chỉ khoá được character sheet (mục thuộc nhóm Nhân vật).", ResultErrorCodes.Validation);
        }

        if (revision.IsLocked)
        {
            return Result<BibleEntryRevisionDto>.Failure("Revision này đã được khoá trước đó.", ResultErrorCodes.Conflict);
        }

        // Chỉ khoá đúng revision đã được Writer/Editor xác nhận. Endorsement gắn với từng revision và mất khi Artist
        // nộp lại concept (về ConceptSubmitted), nên xác nhận của bản cũ không thể mở khoá cho bản mới.
        if (revision.ReviewStatus != BibleRevisionStatus.WriterEndorsed)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Chỉ khoá Canon được revision đã được Writer xác nhận (WriterEndorsed), hiện là {revision.ReviewStatus}.",
                ResultErrorCodes.Conflict);
        }

        revision.Lock(command.UserId);
        entry.UpdatedBy = command.UserId.ToString();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<BibleEntryRevisionDto>.Success(BibleEntryRevisionDto.From(revision));
    }
}
