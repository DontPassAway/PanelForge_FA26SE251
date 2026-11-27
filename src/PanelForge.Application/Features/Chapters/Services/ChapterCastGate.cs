using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Services;

public sealed class ChapterCastGate : IChapterCastGate
{
    private readonly IPanelForgeDbContext _db;
    private readonly IChapterCastService _castService;
    private readonly IChapterCastInheritanceResolver _inheritanceResolver;

    public ChapterCastGate(
        IPanelForgeDbContext db,
        IChapterCastService castService,
        IChapterCastInheritanceResolver inheritanceResolver)
    {
        _db = db;
        _castService = castService;
        _inheritanceResolver = inheritanceResolver;
    }

    public async Task<ChapterCastSubmitGateDto> EvaluateSubmitAsync(Guid chapterId, CancellationToken ct = default)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chapterId, ct);
        if (chapter is null)
            return new ChapterCastSubmitGateDto(false, [new ChapterCastBlockerDto(Guid.Empty, "Chương", null, $"Chương {chapterId} không tồn tại.")]);

        if (chapter.ScriptStatus != ScriptStatus.Frozen)
        {
            var authorId = chapter.ScriptSubmittedById ?? chapter.ScriptFrozenById ?? Guid.Empty;
            await _castService.RecomputeAsync(chapterId, authorId, ct);
        }

        var dbBindings = await _db.ChapterBibleBindings.AsNoTracking()
            .Include(b => b.BibleEntry)
            .Include(b => b.BoundRevision)
            .Where(b => b.ChapterId == chapterId)
            .ToListAsync(ct);
        var localBindings = _db.ChapterBibleBindings.Local.Where(b => b.ChapterId == chapterId).ToList();
        var bindings = localBindings.Union(dbBindings).DistinctBy(b => (b.ChapterId, b.BibleEntryId)).ToList();

        var entryIds = bindings.Select(b => b.BibleEntryId).Distinct().ToList();
        var dbEntries = await _db.BibleEntries.AsNoTracking()
            .Where(e => entryIds.Contains(e.Id))
            .ToListAsync(ct);
        var localEntries = _db.BibleEntries.Local.Where(e => entryIds.Contains(e.Id)).ToList();
        var entryMap = localEntries.Union(dbEntries).DistinctBy(e => e.Id).ToDictionary(e => e.Id);

        var blockers = new List<ChapterCastBlockerDto>();

        foreach (var b in bindings)
        {
            if (b.IsCrowdAtBind) continue;

            entryMap.TryGetValue(b.BibleEntryId, out var entry);
            entry ??= b.BibleEntry;
            if (entry is null) continue;

            if (entry.IsStub)
            {
                blockers.Add(new ChapterCastBlockerDto(
                    entry.Id,
                    entry.Name,
                    b.BoundRevisionId,
                    $"Thực thể '{entry.Name}' chưa được Writer viết định nghĩa (chờ định nghĩa).",
                    "chờ định nghĩa"));
            }
            else if (b.BoundRevisionId is null)
            {
                blockers.Add(new ChapterCastBlockerDto(
                    entry.Id,
                    entry.Name,
                    null,
                    $"Thực thể '{entry.Name}' chưa có bản thiết kế (revision) nào được gán.",
                    "chờ định nghĩa"));
            }
        }

        return new ChapterCastSubmitGateDto(blockers.Count == 0, blockers);
    }

    public async Task<ChapterCastStartGateDto> EvaluateStartAsync(Guid chapterId, CancellationToken ct = default)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chapterId, ct);
        if (chapter is null)
            return new ChapterCastStartGateDto(false, [new ChapterCastBlockerDto(Guid.Empty, "Chương", null, $"Chương {chapterId} không tồn tại.")]);

        if (chapter.ScriptStatus != ScriptStatus.Frozen)
        {
            var authorId = chapter.ScriptSubmittedById ?? chapter.ScriptFrozenById ?? Guid.Empty;
            await _castService.RecomputeAsync(chapterId, authorId, ct);
        }

        var dbBindings = await _db.ChapterBibleBindings.AsNoTracking()
            .Include(b => b.BibleEntry)
            .Include(b => b.BoundRevision)
            .Where(b => b.ChapterId == chapterId)
            .ToListAsync(ct);
        var localBindings = _db.ChapterBibleBindings.Local.Where(b => b.ChapterId == chapterId).ToList();
        var bindings = localBindings.Union(dbBindings).DistinctBy(b => (b.ChapterId, b.BibleEntryId)).ToList();

        var entryIds = bindings.Select(b => b.BibleEntryId).Distinct().ToList();
        var dbEntries = await _db.BibleEntries.AsNoTracking()
            .Include(e => e.Revisions)
            .Where(e => entryIds.Contains(e.Id))
            .ToListAsync(ct);
        var localEntries = _db.BibleEntries.Local.Where(e => entryIds.Contains(e.Id)).ToList();
        var entryMap = localEntries.Union(dbEntries).DistinctBy(e => e.Id).ToDictionary(e => e.Id);

        var revIds = bindings.Where(b => b.BoundRevisionId.HasValue).Select(b => b.BoundRevisionId!.Value).Distinct().ToList();
        var dbRevs = await _db.BibleEntryRevisions.AsNoTracking()
            .Where(r => revIds.Contains(r.Id))
            .ToListAsync(ct);
        var localRevs = _db.BibleEntryRevisions.Local.Where(r => revIds.Contains(r.Id)).ToList();
        var revMap = localRevs.Union(dbRevs).DistinctBy(r => r.Id).ToDictionary(r => r.Id);

        // Load pages and panels of this chapter
        var pages = await _db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId)
            .Select(p => new { p.Id, p.PageNumber })
            .ToListAsync(ct);
        var pageMap = pages.ToDictionary(p => p.Id, p => p.PageNumber);
        var pageIds = pages.Select(p => p.Id).ToList();

        var panels = await _db.Panels.AsNoTracking()
            .Where(p => pageIds.Contains(p.PageId))
            .Select(p => new { p.Id, p.PageId, p.PanelNumber })
            .ToListAsync(ct);
        var panelMap = panels.ToDictionary(p => p.Id);
        var panelIds = panels.Select(p => p.Id).ToList();

        var panelVariants = await _db.PanelBibleVariants.AsNoTracking()
            .Include(v => v.Revision)
            .Where(v => v.PanelId.HasValue && panelIds.Contains(v.PanelId.Value))
            .ToListAsync(ct);

        // Query previous chapter bindings to determine inheritance source when !chosenByWriter
        var previousChapterBindings = await _db.ChapterBibleBindings.AsNoTracking()
            .Where(b => b.Chapter.SeriesId == chapter.SeriesId &&
                        b.Chapter.ChapterNumber < chapter.ChapterNumber &&
                        b.BoundRevisionId != null)
            .Select(b => new { b.Chapter.ChapterNumber, b.BibleEntryId, b.BoundRevisionId })
            .OrderByDescending(b => b.ChapterNumber)
            .ToListAsync(ct);

        var submitBlockers = new List<ChapterCastBlockerDto>();
        var startBlockers = new List<ChapterCastBlockerDto>();

        foreach (var b in bindings)
        {
            if (b.IsCrowdAtBind) continue;

            entryMap.TryGetValue(b.BibleEntryId, out var entry);
            entry ??= b.BibleEntry;
            if (entry is null) continue;

            BibleEntryRevision? boundRev = null;
            if (b.BoundRevisionId.HasValue)
            {
                revMap.TryGetValue(b.BoundRevisionId.Value, out boundRev);
            }
            boundRev ??= b.BoundRevision;

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

            // Submit conditions
            if (entry.IsStub)
            {
                submitBlockers.Add(new ChapterCastBlockerDto(
                    entry.Id,
                    entry.Name,
                    boundRev?.Id,
                    $"Thực thể '{entry.Name}' chưa được Writer viết định nghĩa (chờ định nghĩa).",
                    "chờ định nghĩa"));
            }
            else if (boundRev is null)
            {
                submitBlockers.Add(new ChapterCastBlockerDto(
                    entry.Id,
                    entry.Name,
                    null,
                    $"Thực thể '{entry.Name}' chưa có bản thiết kế (revision) nào được gán.",
                    "chờ định nghĩa"));
            }

            // Start condition: boundRevision must be CanonLocked
            decimal? inheritedChapterNumber = null;
            if (!b.ChosenByWriter && boundRev is not null)
            {
                var prev = previousChapterBindings
                    .FirstOrDefault(pb => pb.BibleEntryId == entry.Id && pb.BoundRevisionId == boundRev.Id);
                inheritedChapterNumber = prev?.ChapterNumber;
            }

            var sourceText = b.ChosenByWriter
                ? "bản do Writer chọn"
                : (inheritedChapterNumber.HasValue ? $"kế thừa từ chương {inheritedChapterNumber}" : "bản mặc định");

            // Non-visual categories (Term, Rule, Event) do not have concept art and cannot be CanonLocked.
            if (entry.IsVisualDesign && (boundRev is null || !boundRev.IsLocked))
            {
                var stageName = boundRev?.StageName ?? "Mặc định";
                var versionNumber = boundRev?.VersionNumber ?? 1;
                var reasonDetail = boundRev is null
                    ? "chờ định nghĩa"
                    : (boundRev.ReviewStatus switch
                    {
                        BibleRevisionStatus.Draft => "chờ Artist vẽ",
                        BibleRevisionStatus.ConceptSubmitted => "chờ Editor xác nhận",
                        BibleRevisionStatus.ReworkRequired => "yêu cầu vẽ lại",
                        _ => "chưa khoá Canon"
                    });

                startBlockers.Add(new ChapterCastBlockerDto(
                    entry.Id,
                    entry.Name,
                    boundRev?.Id,
                    $"Bản '{stageName}' v{versionNumber} chưa được khoá Canon ({reasonDetail}).",
                    $"chờ {entry.Name} {stageName} v{versionNumber}, {sourceText}"));
            }

            // Start condition: all panel variants for visual entities must be CanonLocked
            var entityVariants = panelVariants.Where(v => v.BibleEntryId == entry.Id);
            foreach (var pv in entityVariants)
            {
                if (entry.IsVisualDesign && !pv.Revision.IsLocked)
                {
                    var pan = pv.PanelId.HasValue ? panelMap.GetValueOrDefault(pv.PanelId.Value) : null;
                    var pageNum = pan != null && pageMap.TryGetValue(pan.PageId, out var p) ? p : 0;
                    var panNum = pan?.PanelNumber ?? 0;
                    var pvStage = pv.Revision.StageName ?? "Mặc định";
                    var pvVersion = pv.Revision.VersionNumber;
                    var pvReasonDetail = pv.Revision.ReviewStatus switch
                    {
                        BibleRevisionStatus.Draft => "chờ Artist vẽ",
                        BibleRevisionStatus.ConceptSubmitted => "chờ Editor xác nhận",
                        BibleRevisionStatus.ReworkRequired => "yêu cầu vẽ lại",
                        _ => "chưa khoá Canon"
                    };

                    startBlockers.Add(new ChapterCastBlockerDto(
                        entry.Id,
                        entry.Name,
                        pv.RevisionId,
                        $"Bản theo Panel '{pvStage}' v{pvVersion} (Trang {pageNum}, Panel {panNum}) chưa được khoá Canon ({pvReasonDetail}).",
                        $"Panel {pageNum}.{panNum}"));
                }
            }
        }

        // Add any submit blockers to start blockers
        foreach (var sb in submitBlockers)
        {
            if (!startBlockers.Any(b => b.BibleEntryId == sb.BibleEntryId))
                startBlockers.Add(sb);
        }

        return new ChapterCastStartGateDto(startBlockers.Count == 0, startBlockers);
    }
}
