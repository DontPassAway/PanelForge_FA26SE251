using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.EndorseBibleRevision;

/// <summary>
/// Writer xác nhận mềm (non-blocking) bản vẽ Concept đang ở trạng thái ConceptSubmitted.
/// </summary>
public sealed record EndorseBibleRevisionCommand(
    Guid SeriesId,
    Guid RevisionId,
    Guid UserId,
    bool? Endorse = null
) : IRequest<Result<BibleEntryRevisionDto>>;

public sealed class EndorseBibleRevisionCommandHandler
    : IRequestHandler<EndorseBibleRevisionCommand, Result<BibleEntryRevisionDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public EndorseBibleRevisionCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BibleEntryRevisionDto>> Handle(EndorseBibleRevisionCommand cmd, CancellationToken ct)
    {
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == cmd.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        if (!bibleId.HasValue)
            return Result<BibleEntryRevisionDto>.Failure("Series Bible không tồn tại.", ResultErrorCodes.NotFound);

        var entry = await _dbContext.BibleEntries
            .Include(e => e.Revisions).ThenInclude(r => r.Events)
            .FirstOrDefaultAsync(
                e => e.SeriesBibleId == bibleId.Value && e.Revisions.Any(r => r.Id == cmd.RevisionId),
                ct);

        var revision = entry?.Revisions.FirstOrDefault(r => r.Id == cmd.RevisionId);
        if (entry is null || revision is null)
            return Result<BibleEntryRevisionDto>.Failure($"Revision {cmd.RevisionId} không tồn tại.", ResultErrorCodes.NotFound);

        if (revision.IsLocked || revision.ReviewStatus != BibleRevisionStatus.ConceptSubmitted)
        {
            return Result<BibleEntryRevisionDto>.Failure(
                $"Chỉ xác nhận tạo hình được khi phiên bản đang ở trạng thái ConceptSubmitted, hiện là {revision.ReviewStatus}.",
                ResultErrorCodes.Conflict);
        }

        try
        {
            revision.SetWriterEndorsement(cmd.UserId, cmd.Endorse);
        }
        catch (InvalidOperationException ex)
        {
            return Result<BibleEntryRevisionDto>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }

        entry.UpdatedBy = cmd.UserId.ToString();
        await _dbContext.SaveChangesAsync(ct);

        Dictionary<Guid, string>? userNames = null;
        if (revision.WriterEndorsedByUserId.HasValue && _dbContext.Users is not null)
        {
            var endorserId = revision.WriterEndorsedByUserId.Value;
            var name = await _dbContext.Users
                .AsNoTracking()
                .Where(u => u.Id == endorserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(name))
            {
                userNames = new Dictionary<Guid, string> { [endorserId] = name };
            }
        }

        return Result<BibleEntryRevisionDto>.Success(BibleEntryRevisionDto.From(revision, userNames));
    }
}
