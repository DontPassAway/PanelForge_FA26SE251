using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;

namespace PanelForge.Application.Features.Bible.Queries.GetBibleEntryUsages;

/// <summary>
/// Mục Bible xuất hiện ở những chương nào trong kịch bản (người nói hoặc @Tag), cùng quy tắc với Gate 1
/// (CheckChapterBibleGate, BR-21): mỗi chương cho biết đã có bản Canon hiệu lực tại chương đó chưa (BR-15).
/// </summary>
public sealed record GetBibleEntryUsagesQuery(Guid SeriesId, Guid BibleEntryId) : IRequest<Result<IReadOnlyList<BibleEntryUsageDto>>>;

public sealed class GetBibleEntryUsagesQueryHandler : IRequestHandler<GetBibleEntryUsagesQuery, Result<IReadOnlyList<BibleEntryUsageDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetBibleEntryUsagesQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<BibleEntryUsageDto>>> Handle(GetBibleEntryUsagesQuery request, CancellationToken ct)
    {
        // Entry phải thuộc Bible của seriesId trên route (chống IDOR, giống GetBibleEntryHistory)
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == request.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        var entry = bibleId == null
            ? null
            : await _dbContext.BibleEntries
                .AsNoTracking()
                .Include(e => e.Revisions)
                .FirstOrDefaultAsync(e => e.Id == request.BibleEntryId && e.SeriesBibleId == bibleId.Value, ct);

        if (entry == null)
            return Result<IReadOnlyList<BibleEntryUsageDto>>.Failure(
                $"Mục BibleEntry {request.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);

        var linkedLineIds = await _dbContext.ScriptLineBibleLinks
            .Where(l => l.BibleEntryId == entry.Id)
            .Select(l => l.ScriptLineId)
            .ToListAsync(ct);

        var lines = await _dbContext.ScriptLines
            .Where(l => l.SpeakerCharacterId == entry.Id || linkedLineIds.Contains(l.Id))
            .Select(l => new { l.Id, l.SceneId })
            .ToListAsync(ct);

        if (lines.Count == 0)
            return Result<IReadOnlyList<BibleEntryUsageDto>>.Success([]);

        var sceneIds = lines.Select(l => l.SceneId).Distinct().ToList();
        var sceneChapter = await _dbContext.Scenes
            .Where(s => sceneIds.Contains(s.Id))
            .Select(s => new { s.Id, s.ChapterId })
            .ToDictionaryAsync(s => s.Id, s => s.ChapterId, ct);

        var linesPerChapter = lines
            .Where(l => sceneChapter.ContainsKey(l.SceneId))
            .GroupBy(l => sceneChapter[l.SceneId])
            .ToDictionary(g => g.Key, g => g.Count());

        var chapterIds = linesPerChapter.Keys.ToList();
        var chapters = await _dbContext.Chapters
            .Where(c => c.SeriesId == request.SeriesId && chapterIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ChapterNumber, c.Title, c.IsScriptFrozen })
            .ToListAsync(ct);

        var result = chapters
            .OrderBy(c => c.ChapterNumber)
            .Select(c =>
            {
                var number = (int)c.ChapterNumber;
                var canon = entry.EffectiveRevisionAt(number, canonOnly: true);
                return new BibleEntryUsageDto(
                    ChapterId: c.Id,
                    ChapterNumber: number,
                    ChapterTitle: c.Title,
                    LineCount: linesPerChapter[c.Id],
                    IsScriptFrozen: c.IsScriptFrozen,
                    // Cùng điều kiện "đạt" của Gate 1: mục văn bản chỉ cần bản hiệu lực, mục có hình phải khoá Canon
                    IsCanonSatisfied: canon != null && (!entry.IsVisualDesign || canon.IsLocked));
            })
            .ToList();

        return Result<IReadOnlyList<BibleEntryUsageDto>>.Success(result);
    }
}
