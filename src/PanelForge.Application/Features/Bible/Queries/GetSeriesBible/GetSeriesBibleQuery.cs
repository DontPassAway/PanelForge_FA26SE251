using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Bible.Queries.GetSeriesBible;

public sealed record GetSeriesBibleQuery(Guid SeriesId) : IRequest<Result<SeriesBibleDto>>;

public sealed class GetSeriesBibleQueryHandler : IRequestHandler<GetSeriesBibleQuery, Result<SeriesBibleDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetSeriesBibleQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SeriesBibleDto>> Handle(GetSeriesBibleQuery request, CancellationToken cancellationToken)
    {
        var bible = await _dbContext.SeriesBibles
            .Include(b => b.Entries)
                .ThenInclude(e => e.Revisions)
            .FirstOrDefaultAsync(b => b.SeriesId == request.SeriesId, cancellationToken);

        if (bible == null)
        {
            return Result<SeriesBibleDto>.Failure($"SeriesBible cho Series {request.SeriesId} không tồn tại.");
        }

        var entryDtos = bible.Entries
            .OrderBy(e => e.Category)
            .ThenBy(e => e.Name)
            .Select(e =>
            {
                // Revision có hiệu lực ở chương mới nhất (BR-15), khớp với current state của entry
                var latestRev = e.GetCurrentRevision();

                BibleEntryRevisionDto? revDto = null;
                if (latestRev != null)
                {
                    revDto = new BibleEntryRevisionDto(
                        Id: latestRev.Id,
                        BibleEntryId: latestRev.BibleEntryId,
                        VersionNumber: latestRev.VersionNumber,
                        Summary: latestRev.Summary,
                        SnapshotJson: latestRev.SnapshotJson,
                        ContentHash: latestRev.ContentHash,
                        AuthorId: latestRev.AuthorId,
                        AssociatedChapterId: latestRev.AssociatedChapterId,
                        EffectiveFromChapterNumber: latestRev.EffectiveFromChapterNumber,
                        IsInitialVersion: latestRev.IsInitialVersion,
                        CreatedAt: latestRev.CreatedAt
                    );
                }

                return new BibleEntryDto(
                    Id: e.Id,
                    SeriesBibleId: e.SeriesBibleId,
                    Category: e.Category,
                    Code: e.Code,
                    Name: e.Name,
                    Subtitle: e.Subtitle,
                    Description: e.Description,
                    DetailsJson: e.DetailsJson,
                    ReferenceImageUrl: e.ReferenceImageUrl,
                    Priority: e.Priority,
                    StrictCheck: e.StrictCheck,
                    IsActive: e.IsActive,
                    EffectiveRevision: revDto,
                    CreatedAt: e.CreatedAt,
                    UpdatedAt: e.UpdatedAt
                );
            })
            .ToList();

        return Result<SeriesBibleDto>.Success(new SeriesBibleDto(
            Id: bible.Id,
            SeriesId: bible.SeriesId,
            WorkspaceId: bible.WorkspaceId,
            Name: bible.Name,
            Description: bible.Description,
            Version: bible.Version,
            Entries: entryDtos
        ));
    }
}
