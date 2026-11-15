using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.ReviewBibleEntryRevision;

/// <summary>
/// V2.6 (Q22): Writer, Editor hoặc Producer yêu cầu Artist vẽ lại kèm nhận xét (-> ReworkRequired).
/// Tự động thêm một bình luận gốc gắn với phiên bản concept đang nộp để hiện trong luồng hội thoại.
/// </summary>
public sealed record RequestReworkBibleEntryRevisionCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId,
    Guid UserId,
    string Reason
) : IRequest<Result<BibleEntryRevisionDto>>;

public sealed class RequestReworkBibleEntryRevisionCommandHandler : IRequestHandler<RequestReworkBibleEntryRevisionCommand, Result<BibleEntryRevisionDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public RequestReworkBibleEntryRevisionCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BibleEntryRevisionDto>> Handle(RequestReworkBibleEntryRevisionCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return Result<BibleEntryRevisionDto>.Failure("Lý do yêu cầu vẽ lại không được để trống.", ResultErrorCodes.Validation);

        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == cmd.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        var entry = bibleId is null
            ? null
            : await _dbContext.BibleEntries
                .Include(e => e.Revisions).ThenInclude(r => r.Events)
                .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBibleId == bibleId.Value, ct);

        if (entry is null)
            return Result<BibleEntryRevisionDto>.Failure("Mục Bible không tồn tại.", ResultErrorCodes.NotFound);

        var revision = entry.Revisions.FirstOrDefault(r => r.Id == cmd.RevisionId);
        if (revision is null)
            return Result<BibleEntryRevisionDto>.Failure("Revision không tồn tại.", ResultErrorCodes.NotFound);

        if (revision.IsLocked)
            return Result<BibleEntryRevisionDto>.Failure("Revision đã được khóa Canon, không thể yêu cầu sửa trực tiếp; phải dùng /supersede.", ResultErrorCodes.Conflict);

        var targetConceptEventId = revision.CurrentConceptEventId;

        try
        {
            revision.RequestRework(cmd.UserId, cmd.Reason);
        }
        catch (InvalidOperationException ex)
        {
            return Result<BibleEntryRevisionDto>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }

        if (targetConceptEventId.HasValue)
        {
            var comment = BibleConceptComment.Create(
                revisionId: revision.Id,
                conceptEventId: targetConceptEventId.Value,
                authorId: cmd.UserId,
                body: $"[Yêu cầu vẽ lại]: {cmd.Reason.Trim()}"
            );
            _dbContext.BibleConceptComments.Add(comment);
        }

        entry.UpdatedBy = cmd.UserId.ToString();

        await _dbContext.SaveChangesAsync(ct);

        return Result<BibleEntryRevisionDto>.Success(BibleEntryRevisionDto.From(revision));
    }
}
