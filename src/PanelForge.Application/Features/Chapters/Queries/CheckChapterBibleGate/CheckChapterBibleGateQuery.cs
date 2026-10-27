using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Queries.CheckChapterBibleGate;

/// <summary>
/// V2.5: Cổng kiểm tra Series Bible theo Chương (Chapter-Scoped Bible Gate).
/// Quét toàn bộ speakerCharacterId và bibleEntryIds được dùng trong kịch bản Chương N.
/// Đảm bảo tất cả thực thể xuất hiện trong chương đều có phiên bản CanonLocked có hiệu lực tại Chương N.
/// </summary>
public sealed record CheckChapterBibleGateQuery(Guid ChapterId) : IRequest<Result<ChapterBibleGateResultDto>>;

public sealed class CheckChapterBibleGateQueryHandler : IRequestHandler<CheckChapterBibleGateQuery, Result<ChapterBibleGateResultDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public CheckChapterBibleGateQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ChapterBibleGateResultDto>> Handle(CheckChapterBibleGateQuery query, CancellationToken ct)
    {
        var chapter = await _dbContext.Chapters
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.ChapterId, ct);

        if (chapter is null)
            return Result<ChapterBibleGateResultDto>.Failure($"Chương {query.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var chapterNumber = (int)chapter.ChapterNumber;

        // 1. Lấy tất cả scenes và script lines trong chương
        var sceneIds = await _dbContext.Scenes
            .AsNoTracking()
            .Where(s => s.ChapterId == chapter.Id)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var scriptLines = await _dbContext.ScriptLines
            .AsNoTracking()
            .Where(l => sceneIds.Contains(l.SceneId))
            .ToListAsync(ct);

        var scriptLineIds = scriptLines.Select(l => l.Id).ToList();

        // 2. Thu thập tất cả BibleEntryId được tham chiếu
        var speakerIds = scriptLines
            .Where(l => l.SpeakerCharacterId.HasValue)
            .Select(l => l.SpeakerCharacterId!.Value)
            .Distinct()
            .ToList();

        var linkedEntryIds = await _dbContext.ScriptLineBibleLinks
            .AsNoTracking()
            .Where(l => scriptLineIds.Contains(l.ScriptLineId))
            .Select(l => l.BibleEntryId)
            .Distinct()
            .ToListAsync(ct);

        var allReferencedIds = speakerIds.Union(linkedEntryIds).Distinct().ToList();

        if (allReferencedIds.Count == 0)
        {
            // Không có thực thể Bible nào trong kịch bản -> Gate thông qua
            return Result<ChapterBibleGateResultDto>.Success(new ChapterBibleGateResultDto(
                IsPassed: true,
                TotalReferenced: 0,
                LockedCount: 0,
                PendingCount: 0,
                Items: []
            ));
        }

        // 3. Tải danh sách BibleEntries cùng tất cả Revisions
        var entries = await _dbContext.BibleEntries
            .AsNoTracking()
            .Where(e => allReferencedIds.Contains(e.Id))
            .Include(e => e.Revisions)
            .ToListAsync(ct);

        var items = new List<ChapterBibleGateItemDto>();
        int lockedCount = 0;

        foreach (var entry in entries)
        {
            var effectiveCanon = entry.EffectiveRevisionAt(chapterNumber, canonOnly: true);
            var effectiveAny = entry.EffectiveRevisionAt(chapterNumber, canonOnly: false);
            var latestRev = entry.GetCurrentRevision();

            bool isSatisfied = effectiveCanon != null && (entry.Category != BibleEntryCategory.Character || effectiveCanon.IsLocked);

            if (isSatisfied)
            {
                lockedCount++;
                items.Add(new ChapterBibleGateItemDto(
                    EntryId: entry.Id,
                    Code: entry.Code,
                    Name: entry.Name,
                    Category: entry.Category,
                    CurrentStatus: effectiveCanon!.ReviewStatus,
                    IsLocked: entry.Category == BibleEntryCategory.Character ? effectiveCanon.IsLocked : true,
                    EffectiveFromChapter: effectiveCanon.EffectiveFromChapterNumber,
                    EffectiveToChapter: effectiveCanon.EffectiveToChapterNumber,
                    WarningCode: "OK",
                    Message: entry.Category == BibleEntryCategory.Character
                        ? $"Đã có phiên bản Canon khóa tại Chương {chapterNumber}."
                        : $"Đã có phiên bản hiệu lực tại Chương {chapterNumber}."
                ));
            }
            else
            {
                // Chưa có bản khóa canon có hiệu lực tại chương này
                string warnCode;
                string message;

                if (effectiveAny == null)
                {
                    warnCode = "ENTRY_NOT_YET_EFFECTIVE";
                    message = $"Mục '{entry.Name}' ({entry.Code}) chưa có phiên bản nào bao phủ Chương {chapterNumber} (mới nhất từ Ch.{latestRev?.EffectiveFromChapterNumber ?? 1}).";
                }
                else if (entry.Category == BibleEntryCategory.Character)
                {
                    warnCode = "CHARACTER_NOT_LOCKED";
                    message = $"Mục nhân vật '{entry.Name}' ({entry.Code}) xuất hiện trong kịch bản nhưng chưa được Producer khóa Canon tại Chương {chapterNumber}. Trạng thái hiện tại: {effectiveAny.ReviewStatus}.";
                }
                else
                {
                    warnCode = "ENTRY_NOT_LOCKED";
                    message = $"Mục '{entry.Name}' ({entry.Code}) xuất hiện trong kịch bản nhưng chưa có phiên bản hợp lệ tại Chương {chapterNumber}. Trạng thái hiện tại: {effectiveAny.ReviewStatus}.";
                }

                items.Add(new ChapterBibleGateItemDto(
                    EntryId: entry.Id,
                    Code: entry.Code,
                    Name: entry.Name,
                    Category: entry.Category,
                    CurrentStatus: effectiveAny?.ReviewStatus ?? latestRev?.ReviewStatus ?? BibleRevisionStatus.Draft,
                    IsLocked: false,
                    EffectiveFromChapter: effectiveAny?.EffectiveFromChapterNumber ?? latestRev?.EffectiveFromChapterNumber,
                    EffectiveToChapter: effectiveAny?.EffectiveToChapterNumber ?? latestRev?.EffectiveToChapterNumber,
                    WarningCode: warnCode,
                    Message: message
                ));
            }
        }

        int pendingCount = allReferencedIds.Count - lockedCount;
        bool isPassed = pendingCount == 0;

        return Result<ChapterBibleGateResultDto>.Success(new ChapterBibleGateResultDto(
            IsPassed: isPassed,
            TotalReferenced: allReferencedIds.Count,
            LockedCount: lockedCount,
            PendingCount: pendingCount,
            Items: items
        ));
    }
}
