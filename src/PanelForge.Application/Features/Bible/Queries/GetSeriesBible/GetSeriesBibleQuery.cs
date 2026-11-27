using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

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
                    .ThenInclude(r => r.Events)
            .FirstOrDefaultAsync(b => b.SeriesId == request.SeriesId, cancellationToken);

        if (bible == null)
        {
            // Series tạo trước khi có Bible (dữ liệu cũ/seed): coi là Bible rỗng; mục đầu tiên sẽ tạo Bible (CreateBibleEntry).
            var workspaceId = await _dbContext.Series.AsNoTracking()
                .Where(s => s.Id == request.SeriesId).Select(s => (Guid?)s.WorkspaceId).FirstOrDefaultAsync(cancellationToken);
            return workspaceId == null
                ? Result<SeriesBibleDto>.Failure($"Series {request.SeriesId} không tồn tại.", ResultErrorCodes.NotFound)
                : Result<SeriesBibleDto>.Success(new SeriesBibleDto(Guid.Empty, request.SeriesId, workspaceId.Value, "Series Bible", null, 0, []));
        }

        var endorserIds = bible.Entries
            .SelectMany(e => e.Revisions)
            .Where(r => r.WriterEndorsedByUserId.HasValue)
            .Select(r => r.WriterEndorsedByUserId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string>? userNames = null;
        if (endorserIds.Count > 0 && _dbContext.Users is not null)
        {
            userNames = await _dbContext.Users
                .AsNoTracking()
                .Where(u => endorserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        }

        var entryDtos = bible.Entries
            .OrderBy(e => e.Category)
            .ThenBy(e => e.Name)
            .Select(e => BibleEntryDto.From(e, userNames))
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
