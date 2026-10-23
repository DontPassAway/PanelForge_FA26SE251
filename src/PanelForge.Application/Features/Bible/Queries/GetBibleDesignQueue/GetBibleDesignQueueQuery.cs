using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Queries.GetBibleDesignQueue;

/// <summary>
/// V2.5: Lấy hàng đợi việc thiết kế Bible (Pending Concept Art / Rework) cho Artist.
/// Trả về các mục Bible (Nhân vật, Bối cảnh, Đạo cụ...) đang ở trạng thái Draft hoặc ReworkRequired.
/// </summary>
public sealed record GetBibleDesignQueueQuery(Guid SeriesId) : IRequest<Result<IReadOnlyList<BibleDesignQueueItemDto>>>;

public sealed class GetBibleDesignQueueQueryHandler : IRequestHandler<GetBibleDesignQueueQuery, Result<IReadOnlyList<BibleDesignQueueItemDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetBibleDesignQueueQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<BibleDesignQueueItemDto>>> Handle(GetBibleDesignQueueQuery query, CancellationToken ct)
    {
        var bible = await _dbContext.SeriesBibles
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.SeriesId == query.SeriesId, ct);

        if (bible is null)
            return Result<IReadOnlyList<BibleDesignQueueItemDto>>.Failure("Series Bible không tồn tại.", ResultErrorCodes.NotFound);

        var entries = await _dbContext.BibleEntries
            .AsNoTracking()
            .Where(e => e.SeriesBibleId == bible.Id && e.IsActive)
            .Include(e => e.Revisions)
            .ToListAsync(ct);

        var queue = new List<BibleDesignQueueItemDto>();

        foreach (var entry in entries)
        {
            var latestRev = entry.GetCurrentRevision();
            if (latestRev is null) continue;

            // Đưa vào hàng đợi nếu đang ở trạng thái Draft (cần vẽ Concept) hoặc ReworkRequired (cần vẽ lại)
            if (latestRev.ReviewStatus == BibleRevisionStatus.Draft ||
                latestRev.ReviewStatus == BibleRevisionStatus.ReworkRequired ||
                (entry.Category == BibleEntryCategory.Character && !entry.Revisions.Any(r => r.IsLocked)))
            {
                queue.Add(new BibleDesignQueueItemDto(
                    EntryId: entry.Id,
                    SeriesId: query.SeriesId,
                    Code: entry.Code,
                    Name: entry.Name,
                    Category: entry.Category,
                    Description: entry.Description,
                    ReferenceImageUrl: entry.ReferenceImageUrl,
                    EffectiveFromChapterNumber: latestRev.EffectiveFromChapterNumber,
                    StageName: latestRev.StageName,
                    Status: latestRev.ReviewStatus,
                    VersionNumber: latestRev.VersionNumber,
                    ReworkFeedback: latestRev.ReworkFeedback,
                    CreatedAt: entry.CreatedAt,
                    UpdatedAt: entry.UpdatedAt
                ));
            }
        }

        // Ưu tiên ReworkRequired lên trước, sau đó sắp xếp theo ngày cập nhật mới nhất
        var sorted = queue
            .OrderByDescending(q => q.Status == BibleRevisionStatus.ReworkRequired)
            .ThenByDescending(q => q.UpdatedAt ?? q.CreatedAt)
            .ToList();

        return Result<IReadOnlyList<BibleDesignQueueItemDto>>.Success(sorted);
    }
}
