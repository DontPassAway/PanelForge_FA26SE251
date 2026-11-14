using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Queries.CheckChapterBibleGate;

/// <summary>
/// Cổng kiểm tra Series Bible cũ (được thay thế bằng ChapterCastGate và GET /cast theo BIB-14).
/// Giữ lại để tương thích ngược với FE ChecksTab, sẽ gỡ ở BIB-17.
/// </summary>
[Obsolete("Use GET /cast or ChapterCastGate instead. Scheduled for removal in BIB-17.")]
public sealed record CheckChapterBibleGateQuery(Guid ChapterId) : IRequest<Result<ChapterBibleGateResultDto>>;

[Obsolete("Use GET /cast or ChapterCastGate instead. Scheduled for removal in BIB-17.")]
public sealed class CheckChapterBibleGateQueryHandler : IRequestHandler<CheckChapterBibleGateQuery, Result<ChapterBibleGateResultDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IChapterCastGate _castGate;

    public CheckChapterBibleGateQueryHandler(
        IPanelForgeDbContext dbContext,
        IChapterCastGate castGate)
    {
        _dbContext = dbContext;
        _castGate = castGate;
    }

    public async Task<Result<ChapterBibleGateResultDto>> Handle(CheckChapterBibleGateQuery query, CancellationToken ct)
    {
        var chapter = await _dbContext.Chapters
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.ChapterId, ct);

        if (chapter is null)
            return Result<ChapterBibleGateResultDto>.Failure($"Chương {query.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var gateResult = await _castGate.EvaluateStartAsync(query.ChapterId, ct);

        var dbBindings = await _dbContext.ChapterBibleBindings.AsNoTracking()
            .Include(b => b.BibleEntry)
            .Include(b => b.BoundRevision)
            .Where(b => b.ChapterId == query.ChapterId)
            .ToListAsync(ct);
        var localBindings = _dbContext.ChapterBibleBindings.Local
            .Where(b => b.ChapterId == query.ChapterId).ToList();
        var bindings = dbBindings.Union(localBindings).DistinctBy(b => (b.ChapterId, b.BibleEntryId)).ToList();

        if (bindings.Count == 0)
        {
            return Result<ChapterBibleGateResultDto>.Success(new ChapterBibleGateResultDto(
                IsPassed: gateResult.CanStart,
                TotalReferenced: 0,
                LockedCount: 0,
                PendingCount: gateResult.Blockers.Count,
                Items: []
            ));
        }

        var entryIds = bindings.Select(b => b.BibleEntryId).Distinct().ToList();
        var dbEntries = await _dbContext.BibleEntries.AsNoTracking()
            .Include(e => e.Revisions)
            .Where(e => entryIds.Contains(e.Id))
            .ToListAsync(ct);
        var localEntries = _dbContext.BibleEntries.Local.Where(e => entryIds.Contains(e.Id)).ToList();
        var entryMap = dbEntries.Union(localEntries).DistinctBy(e => e.Id).ToDictionary(e => e.Id);

        var revIds = bindings.Where(b => b.BoundRevisionId.HasValue).Select(b => b.BoundRevisionId!.Value).Distinct().ToList();
        var dbRevs = await _dbContext.BibleEntryRevisions.AsNoTracking()
            .Where(r => revIds.Contains(r.Id))
            .ToListAsync(ct);
        var localRevs = _dbContext.BibleEntryRevisions.Local.Where(r => revIds.Contains(r.Id)).ToList();
        var revMap = dbRevs.Union(localRevs).DistinctBy(r => r.Id).ToDictionary(r => r.Id);

        var items = new List<ChapterBibleGateItemDto>();
        int lockedCount = 0;
        int pendingCount = 0;

        foreach (var binding in bindings)
        {
            entryMap.TryGetValue(binding.BibleEntryId, out var entry);
            entry ??= binding.BibleEntry;
            if (entry is null) continue;

            BibleEntryRevision? boundRev = null;
            if (binding.BoundRevisionId.HasValue)
            {
                revMap.TryGetValue(binding.BoundRevisionId.Value, out boundRev);
            }
            boundRev ??= binding.BoundRevision;

            if (boundRev is null && entry is not null && entry.Revisions.Count > 0)
            {
                boundRev = entry.Revisions
                    .Where(r => r.IsLocked)
                    .OrderByDescending(r => r.VersionNumber)
                    .FirstOrDefault()
                    ?? entry.Revisions
                    .OrderByDescending(r => r.VersionNumber)
                    .FirstOrDefault();
            }

            var blocker = gateResult.Blockers.FirstOrDefault(b => b.BibleEntryId == entry.Id);

            if (blocker is null)
            {
                lockedCount++;
                items.Add(new ChapterBibleGateItemDto(
                    EntryId: entry.Id,
                    Code: entry.Code,
                    Name: entry.Name,
                    Category: entry.Category,
                    CurrentStatus: boundRev?.ReviewStatus ?? BibleRevisionStatus.CanonLocked,
                    IsLocked: true,
                    EffectiveFromChapter: (int)chapter.ChapterNumber,
                    EffectiveToChapter: null,
                    WarningCode: "OK",
                    Message: "Đã có phiên bản Canon khóa tại chương."
                ));
            }
            else
            {
                pendingCount++;
                var warnCode = entry.Category == BibleEntryCategory.Character
                    ? "CHARACTER_NOT_LOCKED"
                    : "ENTRY_NOT_LOCKED";

                items.Add(new ChapterBibleGateItemDto(
                    EntryId: entry.Id,
                    Code: entry.Code,
                    Name: entry.Name,
                    Category: entry.Category,
                    CurrentStatus: boundRev?.ReviewStatus ?? BibleRevisionStatus.Draft,
                    IsLocked: false,
                    EffectiveFromChapter: (int)chapter.ChapterNumber,
                    EffectiveToChapter: null,
                    WarningCode: warnCode,
                    Message: blocker.Reason
                ));
            }
        }

        return Result<ChapterBibleGateResultDto>.Success(new ChapterBibleGateResultDto(
            IsPassed: gateResult.CanStart,
            TotalReferenced: bindings.Count,
            LockedCount: lockedCount,
            PendingCount: pendingCount,
            Items: items
        ));
    }
}
