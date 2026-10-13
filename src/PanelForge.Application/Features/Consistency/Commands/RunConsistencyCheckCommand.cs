using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Consistency.Common;
using PanelForge.Application.Features.Consistency.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Consistency.Commands;

/// <summary>
/// CF3 bước 4: chạy kiểm tra nhất quán bằng code trên script của Chapter, so với Series Bible có hiệu lực ở chương này
/// (BR-15) và ConsistencyRule đang bật. Ghép kết quả với finding cũ theo fingerprint: lỗi mới → Open; vẫn còn → giữ
/// trạng thái (Ignored/Rejected không báo lại, BR-21); không còn → Resolved (nếu Confirmed) hoặc Stale.
/// </summary>
public sealed record RunConsistencyCheckCommand(Guid ChapterId, Guid UserId) : IRequest<Result<ConsistencyCheckResultDto>>;

public sealed class RunConsistencyCheckCommandHandler
    : IRequestHandler<RunConsistencyCheckCommand, Result<ConsistencyCheckResultDto>>
{
    private readonly IPanelForgeDbContext _db;

    public RunConsistencyCheckCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ConsistencyCheckResultDto>> Handle(RunConsistencyCheckCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<ConsistencyCheckResultDto>.Failure($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        // ── Dữ liệu nguồn ──
        var scenes = await _db.Scenes.AsNoTracking().Where(s => s.ChapterId == chapter.Id)
            .Select(s => new { s.Id, s.SceneNumber }).ToListAsync(ct);
        var sceneIds = scenes.Select(s => s.Id).ToList();
        var sceneNumber = scenes.ToDictionary(s => s.Id, s => s.SceneNumber);

        var rawLines = await _db.ScriptLines.AsNoTracking().Where(l => sceneIds.Contains(l.SceneId))
            .Select(l => new { l.Id, l.SceneId, l.LineOrder, l.DialogueText, l.StageDirection, l.SpeakerCharacterId, l.PanelId })
            .ToListAsync(ct);
        var lineIds = rawLines.Select(l => l.Id).ToList();
        var links = (await _db.ScriptLineBibleLinks.AsNoTracking().Where(k => lineIds.Contains(k.ScriptLineId))
                .Select(k => new { k.ScriptLineId, k.BibleEntryId }).ToListAsync(ct))
            .ToLookup(k => k.ScriptLineId, k => k.BibleEntryId);

        var panelIds = rawLines.Where(l => l.PanelId.HasValue).Select(l => l.PanelId!.Value).Distinct().ToList();
        var panels = await _db.Panels.AsNoTracking().Where(p => panelIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ReadingOrder, PageNumber = p.Page.PageNumber })
            .ToDictionaryAsync(p => p.Id, ct);

        var lines = rawLines.Select(l =>
        {
            int? readingOrder = null;
            int? pageNumber = null;
            if (l.PanelId.HasValue && panels.TryGetValue(l.PanelId.Value, out var p))
            {
                readingOrder = p.ReadingOrder;
                pageNumber = p.PageNumber;
            }
            return new CheckLine(
                l.Id, l.SceneId, sceneNumber[l.SceneId], l.LineOrder, l.DialogueText, l.StageDirection,
                l.SpeakerCharacterId, links[l.Id].ToList(), l.PanelId, readingOrder, pageNumber);
        }).ToList();

        var bibleIds = await _db.SeriesBibles.AsNoTracking().Where(b => b.SeriesId == chapter.SeriesId)
            .Select(b => b.Id).ToListAsync(ct);
        var rawEntries = await _db.BibleEntries.AsNoTracking().Where(e => bibleIds.Contains(e.SeriesBibleId))
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.Category,
                e.IsActive,
                e.DetailsJson,
                e.Code,
                FirstEffective = e.Revisions.Select(r => (int?)r.EffectiveFromChapterNumber).Min(),
                FirstLocked = e.Revisions.Where(r => r.LockedAt != null).Select(r => (int?)r.EffectiveFromChapterNumber).Min()
            })
            .ToListAsync(ct);

        var entries = rawEntries.ToDictionary(e => e.Id, e => new CheckEntry(
            e.Id, e.Name, e.Category, e.IsActive, e.FirstEffective, e.FirstLocked,
            detailsJson: e.DetailsJson, code: e.Code));

        var rules = await _db.ConsistencyRules.AsNoTracking().Where(r => r.SeriesId == chapter.SeriesId && r.IsEnabled)
            .Select(r => new CheckRule(r.Id, r.Name, r.RuleType, r.Pattern, r.Severity, r.ConfigurationJson))
            .ToListAsync(ct);

        // ── Kiểm tra + ghép với finding cũ theo fingerprint ──
        var detected = ConsistencyChecker.Check(chapter.ChapterNumber, lines, entries, rules)
            .GroupBy(d => d.Fingerprint).Select(g => g.First()).ToList();
        var existing = await _db.ConsistencyFindings.Where(f => f.ChapterId == chapter.Id).ToListAsync(ct);
        var byFingerprint = existing.ToDictionary(f => f.Fingerprint);
        var now = DateTime.UtcNow;

        var newCount = 0;
        foreach (var d in detected)
        {
            if (byFingerprint.TryGetValue(d.Fingerprint, out var old))
            {
                old.Redetected(now);
                continue;
            }
            var finding = ConsistencyFinding.Create(chapter.Id, d.RuleCode, d.Severity, d.Message, d.Fingerprint,
                d.Evidence, d.SceneId, d.ScriptLineId, d.BibleEntryId, d.ConsistencyRuleId);
            finding.CreatedBy = cmd.UserId.ToString();
            _db.ConsistencyFindings.Add(finding);
            existing.Add(finding);
            newCount++;
        }

        var detectedSet = detected.Select(d => d.Fingerprint).ToHashSet();
        var cleared = 0;
        foreach (var f in existing.Where(f => !detectedSet.Contains(f.Fingerprint)))
        {
            var wasOpen = f.IsOpen;
            f.NoLongerDetected(now);
            if (wasOpen) cleared++;
        }

        await _db.SaveChangesAsync(ct);

        var open = existing.Where(f => f.IsOpen).ToList();
        return Result<ConsistencyCheckResultDto>.Success(new ConsistencyCheckResultDto(
            chapter.Id, now, newCount, open.Count,
            open.Count(f => f.Severity == ConsistencyFindingSeverity.Blocking), cleared,
            ConsistencyFindingOrdering.Order(existing).Select(ConsistencyFindingDto.From).ToList()));
    }
}

internal static class ConsistencyFindingOrdering
{
    /// <summary>Lỗi đang mở trước, nặng trước, rồi theo thời gian phát hiện.</summary>
    public static IEnumerable<ConsistencyFinding> Order(IEnumerable<ConsistencyFinding> findings) =>
        findings.OrderByDescending(f => f.IsOpen).ThenByDescending(f => f.Severity).ThenBy(f => f.CreatedAt);
}
