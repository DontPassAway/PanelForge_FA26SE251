using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Services;

public interface IChapterCastService
{
    /// <summary>
    /// Tính lại toàn bộ Danh sách & Bản đồ thực thể chương (BIB-12, 3A):
    /// 1. SpeakerResolver: khớp người nói nguyên chuỗi với tên chuẩn hoá; không khớp -> tạo stub Nhân vật.
    /// 2. Đồng bộ chapter_bible_bindings (nguồn Speaker/Highlight, kế thừa, is_crowd_at_bind).
    /// 3. Dọn mục stub thừa.
    /// </summary>
    Task RecomputeAsync(Guid chapterId, Guid authorUserId, CancellationToken ct = default);
}

public sealed class ChapterCastService : IChapterCastService
{
    private readonly IPanelForgeDbContext _db;
    private readonly IChapterCastInheritanceResolver _inheritanceResolver;

    public ChapterCastService(
        IPanelForgeDbContext db,
        IChapterCastInheritanceResolver inheritanceResolver)
    {
        _db = db;
        _inheritanceResolver = inheritanceResolver;
    }

    public async Task RecomputeAsync(Guid chapterId, Guid authorUserId, CancellationToken ct = default)
    {
        var chapter = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == chapterId, ct);
        if (chapter is null)
            return;

        var seriesBible = await _db.SeriesBibles.FirstOrDefaultAsync(b => b.SeriesId == chapter.SeriesId, ct)
            ?? _db.SeriesBibles.Local.FirstOrDefault(b => b.SeriesId == chapter.SeriesId);
        if (seriesBible is null)
        {
            var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == chapter.SeriesId, ct);
            var workspaceId = series?.WorkspaceId ?? Guid.Empty;
            seriesBible = SeriesBible.Create(chapter.SeriesId, workspaceId, "Series Bible");
            _db.SeriesBibles.Add(seriesBible);
        }

        // 1. Lấy toàn bộ dòng kịch bản thuộc chương (sắp xếp theo thứ tự để giữ tên hiển thị đầu tiên)
        var lines = await _db.ScriptLines
            .Where(l => (l.PanelId != null && l.Panel!.Page.ChapterId == chapterId) || l.Scene.ChapterId == chapterId)
            .OrderBy(l => l.LineOrder)
            .ToListAsync(ct);

        // 2. SpeakerResolver: chuẩn hoá người nói của dòng Dialogue & Thought, khớp tên mục Nhân vật (tên chính + bí danh)
        var candidates = SpeakerResolver.ExtractSpeakerCandidates(lines);
        var normKeys = candidates.Select(c => c.NormalizedName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingEntries = await _db.BibleEntries
            .Where(e => e.SeriesBibleId == seriesBible.Id
                && e.Category == BibleEntryCategory.Character
                && e.IsActive)
            .ToListAsync(ct);

        var resolvedSpeakerMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in existingEntries)
        {
            if (normKeys.Contains(entry.NormalizedName))
            {
                resolvedSpeakerMap[entry.NormalizedName] = entry.Id;
            }
        }

        // Sau tên chính, khớp tiếp theo danh sách Aliases (không ghi đè tên chính nếu trùng)
        foreach (var entry in existingEntries)
        {
            foreach (var alias in entry.Aliases)
            {
                var aliasNorm = BibleNameNormalizer.Normalize(alias);
                if (!string.IsNullOrEmpty(aliasNorm) && normKeys.Contains(aliasNorm) && !resolvedSpeakerMap.ContainsKey(aliasNorm))
                {
                    resolvedSpeakerMap[aliasNorm] = entry.Id;
                }
            }
        }

        foreach (var cand in candidates)
        {
            if (resolvedSpeakerMap.ContainsKey(cand.NormalizedName))
                continue;

            // Kiểm tra trong bộ nhớ local (tránh trùng lặp trong cùng transaction)
            var localEntry = _db.BibleEntries.Local.FirstOrDefault(e =>
                e.SeriesBibleId == seriesBible.Id
                && e.Category == BibleEntryCategory.Character
                && e.IsActive
                && e.NormalizedName == cand.NormalizedName);
            if (localEntry is not null)
            {
                resolvedSpeakerMap[cand.NormalizedName] = localEntry.Id;
                continue;
            }

            // Không khớp -> tạo mục Nhân vật Draft (CreateStub, IsStub = true, mô tả rỗng)
            var stub = BibleEntry.CreateStub(seriesBible.Id, BibleEntryCategory.Character, cand.DisplayName, description: string.Empty);
            _db.BibleEntries.Add(stub);

            _db.AuditLogs.Add(AuditLog.Create(
                action: "AutoCreateFromSpeaker",
                entityName: "BibleEntry",
                entityId: stub.Id.ToString(),
                userId: authorUserId,
                details: $"Tự tạo từ người nói, chương {chapter.ChapterNumber}"));

            resolvedSpeakerMap[cand.NormalizedName] = stub.Id;
        }

        // 3. Tổng hợp người nói + script_line_bible_links + panel_bible_variants
        var lineIds = lines.Select(l => l.Id).ToList();
        var links = await _db.ScriptLineBibleLinks
            .Where(k => lineIds.Contains(k.ScriptLineId))
            .ToListAsync(ct);

        var pageIds = await _db.Pages.Where(p => p.ChapterId == chapterId).Select(p => p.Id).ToListAsync(ct);
        var panelIds = await _db.Panels.Where(p => pageIds.Contains(p.PageId)).Select(p => p.Id).ToListAsync(ct);
        var panelVariants = await _db.PanelBibleVariants
            .Where(v => v.PanelId.HasValue && panelIds.Contains(v.PanelId.Value))
            .ToListAsync(ct);

        var speakerEntryIds = candidates
            .Where(c => resolvedSpeakerMap.ContainsKey(c.NormalizedName))
            .Select(c => resolvedSpeakerMap[c.NormalizedName])
            .ToHashSet();

        var highlightEntryIds = links.Select(l => l.BibleEntryId).ToHashSet();
        var panelVariantEntryIds = panelVariants.Select(v => v.BibleEntryId).ToHashSet();

        var allReferencedIds = new HashSet<Guid>(speakerEntryIds);
        allReferencedIds.UnionWith(highlightEntryIds);
        allReferencedIds.UnionWith(panelVariantEntryIds);

        // 4. Đồng bộ chapter_bible_bindings
        var dbBindings = await _db.ChapterBibleBindings
            .Where(b => b.ChapterId == chapterId)
            .ToListAsync(ct);
        var localBindings = _db.ChapterBibleBindings.Local.Where(b => b.ChapterId == chapterId).ToList();
        var currentBindings = dbBindings.Union(localBindings).DistinctBy(b => (b.ChapterId, b.BibleEntryId)).ToList();

        // Xoá các binding không còn được nhắc tới
        foreach (var oldBinding in currentBindings.Where(b => !allReferencedIds.Contains(b.BibleEntryId)))
        {
            _db.ChapterBibleBindings.Remove(oldBinding);
        }

        var referencedEntries = await _db.BibleEntries
            .Where(e => allReferencedIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        foreach (var entryId in allReferencedIds)
        {
            var isSpeaker = speakerEntryIds.Contains(entryId);
            var source = isSpeaker ? ChapterBibleBindingSource.Speaker : ChapterBibleBindingSource.Highlight;
            var entry = referencedEntries.GetValueOrDefault(entryId);
            var isCrowd = entry?.IsCrowd ?? false;

            var existingBinding = currentBindings.FirstOrDefault(b => b.BibleEntryId == entryId);
            if (existingBinding is null)
            {
                var binding = ChapterBibleBinding.Create(
                    chapterId,
                    entryId,
                    boundRevisionId: null,
                    source: source,
                    chosenByWriter: false,
                    isCrowdAtBind: isCrowd,
                    boundById: authorUserId);
                _db.ChapterBibleBindings.Add(binding);
            }
            else
            {
                // Ưu tiên source: Speaker đè Highlight
                if (existingBinding.Source != source && isSpeaker)
                {
                    existingBinding.UpdateBinding(
                        existingBinding.BoundRevisionId,
                        ChapterBibleBindingSource.Speaker,
                        existingBinding.ChosenByWriter,
                        existingBinding.BoundById);
                }

                // Cập nhật is_crowd_at_bind nếu chương chưa Frozen
                if (chapter.ScriptStatus != ScriptStatus.Frozen)
                {
                    existingBinding.SetCrowdAtBind(isCrowd);
                }
            }
        }

        // 5. Tính lại bản kế thừa cho các binding và dây chuyền chương sau chưa Frozen qua IChapterCastInheritanceResolver
        await _inheritanceResolver.ResolveInheritedAsync(chapter.SeriesId, chapter.ChapterNumber, ct);

        // 6. Dọn mục thừa: IsStub, hệ thống tạo, không còn binding và tham chiếu nào, chỉ có sự kiện Created -> xoá
        var stubCandidates = await _db.BibleEntries
            .Include(e => e.Revisions)
                .ThenInclude(r => r.Events)
            .Where(e => e.SeriesBibleId == seriesBible.Id && e.IsStub)
            .ToListAsync(ct);

        if (stubCandidates.Count > 0)
        {
            var stubIds = stubCandidates.Select(s => s.Id).ToList();

            var otherChapterBoundIds = await _db.ChapterBibleBindings
                .Where(b => b.ChapterId != chapterId && stubIds.Contains(b.BibleEntryId))
                .Select(b => b.BibleEntryId)
                .Distinct()
                .ToListAsync(ct);

            var otherChapterVariantIds = await _db.PanelBibleVariants
                .Where(v => v.Panel.Page.ChapterId != chapterId && stubIds.Contains(v.BibleEntryId))
                .Select(v => v.BibleEntryId)
                .Distinct()
                .ToListAsync(ct);

            var otherChapterLinkIds = await _db.ScriptLineBibleLinks
                .Where(l => l.ScriptLine.Scene.ChapterId != chapterId && stubIds.Contains(l.BibleEntryId))
                .Select(l => l.BibleEntryId)
                .Distinct()
                .ToListAsync(ct);

            var inUseSet = new HashSet<Guid>(allReferencedIds);
            inUseSet.UnionWith(otherChapterBoundIds);
            inUseSet.UnionWith(otherChapterVariantIds);
            inUseSet.UnionWith(otherChapterLinkIds);

            foreach (var stub in stubCandidates)
            {
                if (inUseSet.Contains(stub.Id))
                    continue;

                var onlyCreated = stub.Revisions.All(r => !r.HasReviewHistory);

                if (onlyCreated)
                {
                    _db.BibleEntries.Remove(stub);
                }
            }
        }
    }
}
