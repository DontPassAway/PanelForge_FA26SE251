using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Infrastructure.Services;

/// <summary>
/// Tính lại bản kế thừa của Danh sách thực thể chương (BIB-13, Q23, Q24).
/// Dùng chung cho: commit kịch bản (BIB-12), Writer đổi bản (SetChapterCastRevision),
/// Editor khoá bản thay thế (BIB-10).
/// </summary>
public sealed class ChapterCastInheritanceResolver : IChapterCastInheritanceResolver
{
    private readonly IPanelForgeDbContext _dbContext;

    public ChapterCastInheritanceResolver(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task ResolveInheritedAsync(Guid seriesId, int fromChapterNumber, CancellationToken cancellationToken = default)
    {
        // 1. Tải toàn bộ chương cùng Series theo thứ tự số chương tăng dần
        var chapters = await _dbContext.Chapters
            .Where(c => c.SeriesId == seriesId)
            .OrderBy(c => c.ChapterNumber)
            .ToListAsync(cancellationToken);

        if (chapters.Count == 0) return;

        var chapterIds = chapters.Select(c => c.Id).ToList();

        // 2. Tải toàn bộ bindings của các chương này (bao gồm cả Local chưa SaveChanges)
        var dbBindings = await _dbContext.ChapterBibleBindings
            .Where(b => chapterIds.Contains(b.ChapterId))
            .ToListAsync(cancellationToken);
        var localBindings = _dbContext.ChapterBibleBindings.Local
            .Where(b => chapterIds.Contains(b.ChapterId)).ToList();
        var bindings = dbBindings.Union(localBindings).DistinctBy(b => (b.ChapterId, b.BibleEntryId)).ToList();

        if (bindings.Count == 0) return;

        // 3. Tải SeriesBible và các BibleEntry cùng Revisions và Events để duyệt chuỗi thay thế
        var seriesBibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == seriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var dbEntries = seriesBibleId == null
            ? new List<BibleEntry>()
            : await _dbContext.BibleEntries
                .Where(e => e.SeriesBibleId == seriesBibleId.Value)
                .Include(e => e.Revisions).ThenInclude(r => r.Events)
                .ToListAsync(cancellationToken);
        var localEntries = _dbContext.BibleEntries.Local
            .Where(e => seriesBibleId != null && e.SeriesBibleId == seriesBibleId.Value).ToList();
        var entries = dbEntries.Union(localEntries).DistinctBy(e => e.Id).ToList();

        var entriesById = entries.ToDictionary(e => e.Id);

        // 4. Duyệt các chương theo thứ tự ChapterNumber tăng dần
        foreach (var chapter in chapters)
        {
            // Q23: chương đã chốt (kể cả khi được mở lại) không tự kế thừa lại
            var wasEverFrozen = chapter.ScriptStatus == ScriptStatus.Frozen ||
                                chapter.IsScriptFrozen ||
                                chapter.ScriptFrozenAt.HasValue ||
                                chapter.FrozenScriptVersionId.HasValue ||
                                chapter.ScriptReopenStatus == ScriptReopenStatus.Approved;
            var isEligible = chapter.ChapterNumber >= fromChapterNumber && !wasEverFrozen;

            if (!isEligible)
            {
                // Chương đã Frozen/chốt trước đó hoặc số chương < fromChapterNumber thì giữ nguyên binding
                continue;
            }

            var chapterBindings = bindings.Where(b => b.ChapterId == chapter.Id).ToList();
            var prevChapters = chapters
                .Where(prev => prev.ChapterNumber < chapter.ChapterNumber)
                .OrderByDescending(prev => prev.ChapterNumber)
                .ToList();

            foreach (var binding in chapterBindings)
            {
                // Dòng do Writer tự chọn (chosen_by_writer = true) giữ nguyên
                if (binding.ChosenByWriter) continue;

                entriesById.TryGetValue(binding.BibleEntryId, out var entry);
                if (entry == null) continue;

                // Tìm chương gần nhất phía trước có thực thể đó
                ChapterBibleBinding? closestPrevBinding = null;
                foreach (var prev in prevChapters)
                {
                    var pb = bindings.FirstOrDefault(b => b.ChapterId == prev.Id && b.BibleEntryId == binding.BibleEntryId);
                    if (pb != null && pb.BoundRevisionId.HasValue)
                    {
                        closestPrevBinding = pb;
                        break;
                    }
                }

                Guid? targetRevisionId = null;
                if (closestPrevBinding?.BoundRevisionId != null)
                {
                    // Đi theo chuỗi thay thế tới bản đã khoá Canon mới nhất
                    targetRevisionId = ResolveLatestLockedReplacement(entry, closestPrevBinding.BoundRevisionId.Value);
                }
                else
                {
                    // Chưa có chương nào phía trước: lấy bản CanonLocked mới nhất (chưa Superseded) của biến thể mặc định,
                    // nếu chưa có bản khoá thì lấy bản mới nhất của biến thể mặc định.
                    targetRevisionId = ResolveDefaultVariantRevisionId(entry);
                }

                if (binding.BoundRevisionId != targetRevisionId)
                {
                    binding.UpdateBinding(
                        boundRevisionId: targetRevisionId,
                        source: binding.Source,
                        chosenByWriter: false,
                        boundById: binding.BoundById);
                }
            }
        }
    }

    private static Guid ResolveLatestLockedReplacement(BibleEntry entry, Guid initialRevisionId)
    {
        var currentId = initialRevisionId;

        // Đi theo chuỗi supersedes_revision_id tới bản đã khoá mới nhất
        while (true)
        {
            var nextLocked = entry.Revisions
                .Where(r => r.SupersedesRevisionId == currentId && r.IsLocked)
                .OrderByDescending(r => r.VersionNumber)
                .FirstOrDefault();

            if (nextLocked == null) break;
            currentId = nextLocked.Id;
        }

        return currentId;
    }

    private static Guid? ResolveDefaultVariantRevisionId(BibleEntry entry)
    {
        if (entry.Revisions.Count == 0) return null;

        // Biến thể mặc định = bản đầu tiên của mục (versionNumber nhỏ nhất)
        var firstRev = entry.Revisions.OrderBy(r => r.VersionNumber).FirstOrDefault();
        var defaultStageName = firstRev?.StageName;

        var defaultVariantRevisions = entry.Revisions
            .Where(r => string.Equals(r.StageName, defaultStageName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (defaultVariantRevisions.Count == 0) return firstRev?.Id;

        // Bản CanonLocked mới nhất (chưa Superseded)
        var lockedNonSuperseded = defaultVariantRevisions
            .Where(r => r.IsLocked && !IsRevisionSuperseded(r, entry.Revisions))
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefault();

        if (lockedNonSuperseded != null)
        {
            return lockedNonSuperseded.Id;
        }

        // Chưa có bản khoá: bản mới nhất của biến thể mặc định
        return defaultVariantRevisions
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefault()?.Id;
    }

    private static bool IsRevisionSuperseded(BibleEntryRevision rev, IEnumerable<BibleEntryRevision> allRevs)
    {
        return rev.Events.Any(e => e.Type == BibleRevisionEventType.Superseded)
            || allRevs.Any(other => other.SupersedesRevisionId == rev.Id && other.IsLocked);
    }
}
