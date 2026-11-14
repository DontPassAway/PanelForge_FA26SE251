using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Entities.Bible;

namespace PanelForge.Application.Features.Bible.Commands.ConceptComments;

public sealed record CreateBibleConceptCommentCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId,
    Guid UserId,
    CreateConceptCommentRequest Body
) : IRequest<Result<BibleConceptCommentDto>>;

public sealed class CreateBibleConceptCommentCommandHandler
    : IRequestHandler<CreateBibleConceptCommentCommand, Result<BibleConceptCommentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public CreateBibleConceptCommentCommandHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<BibleConceptCommentDto>> Handle(CreateBibleConceptCommentCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Body.Body))
            return Result<BibleConceptCommentDto>.Failure("Nội dung góp ý không được để trống.", ResultErrorCodes.Validation);

        var bibleId = await _db.SeriesBibles
            .Where(b => b.SeriesId == cmd.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        var entry = bibleId is null
            ? null
            : await _db.BibleEntries
                .Include(e => e.Revisions).ThenInclude(r => r.Events)
                .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBibleId == bibleId.Value, ct);

        if (entry is null)
            return Result<BibleConceptCommentDto>.Failure("Mục Bible không tồn tại.", ResultErrorCodes.NotFound);

        var revision = entry.Revisions.FirstOrDefault(r => r.Id == cmd.RevisionId);
        if (revision is null)
            return Result<BibleConceptCommentDto>.Failure("Revision không tồn tại.", ResultErrorCodes.NotFound);

        var conceptEventId = cmd.Body.ConceptEventId ?? revision.CurrentConceptEventId;
        if (!conceptEventId.HasValue)
            return Result<BibleConceptCommentDto>.Failure("Revision chưa có phiên bản concept nào để góp ý.", ResultErrorCodes.Conflict);

        var conceptEvent = revision.Events.FirstOrDefault(e => e.Id == conceptEventId.Value);
        if (conceptEvent is null)
            return Result<BibleConceptCommentDto>.Failure("Phiên bản concept không tồn tại.", ResultErrorCodes.NotFound);

        BibleConceptComment comment;
        try
        {
            comment = BibleConceptComment.Create(
                revisionId: revision.Id,
                conceptEventId: conceptEventId.Value,
                authorId: cmd.UserId,
                body: cmd.Body.Body,
                region: cmd.Body.Region
            );
        }
        catch (ArgumentException ex)
        {
            return Result<BibleConceptCommentDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        _db.BibleConceptComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        return Result<BibleConceptCommentDto>.Success(BibleConceptCommentDto.From(comment));
    }
}

public sealed record ReplyBibleConceptCommentCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId,
    Guid CommentId,
    Guid UserId,
    ReplyConceptCommentRequest Body
) : IRequest<Result<BibleConceptCommentDto>>;

public sealed class ReplyBibleConceptCommentCommandHandler
    : IRequestHandler<ReplyBibleConceptCommentCommand, Result<BibleConceptCommentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ReplyBibleConceptCommentCommandHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<BibleConceptCommentDto>> Handle(ReplyBibleConceptCommentCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Body.Body))
            return Result<BibleConceptCommentDto>.Failure("Nội dung trả lời không được để trống.", ResultErrorCodes.Validation);

        var bibleId = await _db.SeriesBibles
            .Where(b => b.SeriesId == cmd.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        var entryExists = bibleId is not null && await _db.BibleEntries
            .AnyAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBibleId == bibleId.Value, ct);
        if (!entryExists)
            return Result<BibleConceptCommentDto>.Failure("Mục Bible không tồn tại.", ResultErrorCodes.NotFound);

        var parent = await _db.BibleConceptComments
            .FirstOrDefaultAsync(c => c.Id == cmd.CommentId && c.RevisionId == cmd.RevisionId && !c.IsDeleted, ct);

        if (parent is null)
        {
            return Result<BibleConceptCommentDto>.Failure("Bình luận gốc không tồn tại.", ResultErrorCodes.NotFound);
        }

        // Chỉ cho phép reply 1 cấp: nếu parent đã là reply thì dùng parent của nó
        var targetParentId = parent.ParentCommentId ?? parent.Id;

        BibleConceptComment reply;
        try
        {
            reply = BibleConceptComment.Create(
                revisionId: parent.RevisionId,
                conceptEventId: parent.ConceptEventId,
                authorId: cmd.UserId,
                body: cmd.Body.Body,
                parentCommentId: targetParentId
            );
        }
        catch (ArgumentException ex)
        {
            return Result<BibleConceptCommentDto>.Failure(ex.Message, ResultErrorCodes.Validation);
        }

        _db.BibleConceptComments.Add(reply);
        await _db.SaveChangesAsync(ct);

        return Result<BibleConceptCommentDto>.Success(BibleConceptCommentDto.From(reply));
    }
}

public sealed record ResolveBibleConceptCommentCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId,
    Guid CommentId,
    Guid UserId,
    bool Resolve = true
) : IRequest<Result<BibleConceptCommentDto>>;

public sealed class ResolveBibleConceptCommentCommandHandler
    : IRequestHandler<ResolveBibleConceptCommentCommand, Result<BibleConceptCommentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ResolveBibleConceptCommentCommandHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<BibleConceptCommentDto>> Handle(ResolveBibleConceptCommentCommand cmd, CancellationToken ct)
    {
        var bibleId = await _db.SeriesBibles
            .Where(b => b.SeriesId == cmd.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        var entryExists = bibleId is not null && await _db.BibleEntries
            .AnyAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBibleId == bibleId.Value, ct);
        if (!entryExists)
            return Result<BibleConceptCommentDto>.Failure("Mục Bible không tồn tại.", ResultErrorCodes.NotFound);

        var comment = await _db.BibleConceptComments
            .Include(c => c.Replies.Where(r => !r.IsDeleted))
            .FirstOrDefaultAsync(c => c.Id == cmd.CommentId && c.RevisionId == cmd.RevisionId && !c.IsDeleted, ct);

        if (comment is null)
        {
            return Result<BibleConceptCommentDto>.Failure("Bình luận không tồn tại.", ResultErrorCodes.NotFound);
        }

        if (cmd.Resolve)
            comment.Resolve(cmd.UserId);
        else
            comment.Unresolve();

        await _db.SaveChangesAsync(ct);

        return Result<BibleConceptCommentDto>.Success(BibleConceptCommentDto.From(comment));
    }
}
