using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Queries.GetBibleDesignQueue;

/// <summary>
/// V2.5: Lấy hàng đợi việc thiết kế Bible (Pending Concept Art / Rework) cho Artist.
/// Trả về từng giai đoạn của mục Bible (Nhân vật, Bối cảnh, Đạo cụ...) đang ở trạng thái Draft, ReworkRequired hoặc ConceptSubmitted.
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

        // Mỗi giai đoạn là một việc vẽ riêng: xét bản mới nhất của từng giai đoạn, không chỉ giai đoạn hiện hành,
        // để giai đoạn cũ (vd. "Lúc nhỏ") cũng được vẽ và khoá.
        foreach (var entry in entries)
        foreach (var latestRev in entry.LatestRevisionPerStage())
        {
            // Đưa vào hàng đợi việc vẽ nếu đang ở trạng thái Draft (chưa nộp Concept), ReworkRequired (cần vẽ lại),
            // hoặc ConceptSubmitted (đã nộp nhưng chưa khoá, cho phép Artist xem lại hoặc nộp lại nếu gửi nhầm)
            if (latestRev.ReviewStatus == BibleRevisionStatus.Draft ||
                latestRev.ReviewStatus == BibleRevisionStatus.ReworkRequired ||
                (latestRev.ReviewStatus == BibleRevisionStatus.ConceptSubmitted && !latestRev.IsLocked))
            {
                queue.Add(new BibleDesignQueueItemDto(
                    EntryId: entry.Id,
                    SeriesId: query.SeriesId,
                    Code: entry.Code,
                    Name: entry.Name,
                    Category: entry.Category,
                    Description: entry.Description,
                    ReferenceImageUrl: latestRev.ConceptImageUrl ?? entry.ReferenceImageUrl,
                    EffectiveFromChapterNumber: latestRev.EffectiveFromChapterNumber,
                    StageName: latestRev.StageName,
                    Status: latestRev.ReviewStatus,
                    VersionNumber: latestRev.VersionNumber,
                    ReworkFeedback: latestRev.ReworkFeedback,
                    CreatedAt: entry.CreatedAt,
                    UpdatedAt: entry.UpdatedAt,
                    RevisionId: latestRev.Id
                ));
            }
        }

        // Ưu tiên ReworkRequired lên trước, sau đó tới Draft, rồi tới ConceptSubmitted
        var sorted = queue
            .OrderByDescending(q => q.Status == BibleRevisionStatus.ReworkRequired)
            .ThenByDescending(q => q.Status == BibleRevisionStatus.Draft)
            .ThenByDescending(q => q.UpdatedAt ?? q.CreatedAt)
            .ToList();

        return Result<IReadOnlyList<BibleDesignQueueItemDto>>.Success(sorted);
    }
}
