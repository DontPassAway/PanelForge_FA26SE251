using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.ReviewBibleEntryRevision;

/// <summary>
/// Editor duyệt thiết kế của Artist (ConceptSubmitted -> WriterEndorsed; mục chỉ có văn bản: Draft -> WriterEndorsed),
/// sau đó Producer khoá Canon. Producer không tự duyệt để luôn có hai người kiểm.
/// </summary>
public sealed record EndorseBibleEntryRevisionCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId,
    Guid UserId,
    string? Note = null
) : IRequest<Result<BibleEntryRevisionDto>>;

public sealed class EndorseBibleEntryRevisionCommandHandler : IRequestHandler<EndorseBibleEntryRevisionCommand, Result<BibleEntryRevisionDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public EndorseBibleEntryRevisionCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BibleEntryRevisionDto>> Handle(EndorseBibleEntryRevisionCommand cmd, CancellationToken ct)
    {
        var entry = await _dbContext.BibleEntries
            .Include(e => e.Revisions).ThenInclude(r => r.Events)
            .Include(e => e.SeriesBible)
            .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBible.SeriesId == cmd.SeriesId, ct);

        if (entry is null)
            return Result<BibleEntryRevisionDto>.Failure("Mục Bible không tồn tại.", ResultErrorCodes.NotFound);

        var revision = entry.Revisions.FirstOrDefault(r => r.Id == cmd.RevisionId);
        if (revision is null)
            return Result<BibleEntryRevisionDto>.Failure("Revision không tồn tại.", ResultErrorCodes.NotFound);

        if (revision.IsLocked)
            return Result<BibleEntryRevisionDto>.Failure("Revision đã được khóa Canon, không thể chỉnh sửa trạng thái kiểm duyệt.", ResultErrorCodes.Conflict);

        try
        {
            revision.Endorse(cmd.UserId, requiresConcept: entry.IsVisualDesign);
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

/// <summary>
/// Editor (khi duyệt) hoặc Producer (khi khoá) thấy thiết kế chưa đúng -> Yêu cầu Artist vẽ lại kèm nhận xét (-> ReworkRequired).
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

        var entry = await _dbContext.BibleEntries
            .Include(e => e.Revisions).ThenInclude(r => r.Events)
            .Include(e => e.SeriesBible)
            .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBible.SeriesId == cmd.SeriesId, ct);

        if (entry is null)
            return Result<BibleEntryRevisionDto>.Failure("Mục Bible không tồn tại.", ResultErrorCodes.NotFound);

        var revision = entry.Revisions.FirstOrDefault(r => r.Id == cmd.RevisionId);
        if (revision is null)
            return Result<BibleEntryRevisionDto>.Failure("Revision không tồn tại.", ResultErrorCodes.NotFound);

        if (revision.IsLocked)
            return Result<BibleEntryRevisionDto>.Failure("Revision đã được khóa Canon, không thể yêu cầu sửa trực tiếp; phải dùng /supersede.", ResultErrorCodes.Conflict);

        try
        {
            revision.RequestRework(cmd.UserId, cmd.Reason);
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
