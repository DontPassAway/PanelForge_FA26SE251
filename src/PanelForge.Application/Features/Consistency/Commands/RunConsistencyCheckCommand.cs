using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Services;
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
            .Select(l => new { l.Id, l.SceneId, l.LineOrder, l.DialogueText, l.StageDirection, l.PanelId, l.NarrationMode, l.SpeakerLabel })
            .ToListAsync(ct);
        var lineIds = rawLines.Select(l => l.Id).ToList();
        var links = (await _db.ScriptLineBibleLinks.AsNoTracking().Where(k => lineIds.Contains(k.ScriptLineId))
                .Select(k => new { k.ScriptLineId, k.BibleEntryId }).ToListAsync(ct))
            .ToLookup(k => k.ScriptLineId, k => k.BibleEntryId);

        var panelIds = rawLines.Where(l => l.PanelId.HasValue).Select(l => l.PanelId!.Value).Distinct().ToList();
        var panels = await _db.Panels.AsNoTracking().Where(p => panelIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ReadingOrder, PageNumber = p.Page.PageNumber })
            .ToDictionaryAsync(p => p.Id, ct);

        var bibleNames = await _db.BibleEntries.AsNoTracking()
            .Where(e => e.SeriesBible.SeriesId == chapter.SeriesId)
            .ToListAsync(ct);
        var speakerToEntryMap = bibleNames
            .ToDictionary(e => e.NormalizedName, e => e.Id);

        var lines = rawLines.Select(l =>
        {
            int? readingOrder = null;
            int? pageNumber = null;
            if (l.PanelId.HasValue && panels.TryGetValue(l.PanelId.Value, out var p))
            {
                readingOrder = p.ReadingOrder;
                pageNumber = p.PageNumber;
            }

            Guid? speakerId = null;
            if (!string.IsNullOrWhiteSpace(l.SpeakerLabel))
            {
                var norm = SpeakerResolver.NormalizeSpeaker(l.SpeakerLabel);
                if (speakerToEntryMap.TryGetValue(norm, out var sid))
                    speakerId = sid;
            }

            return new CheckLine(
                l.Id, l.SceneId, sceneNumber[l.SceneId], l.LineOrder, l.DialogueText, l.StageDirection,
                speakerId, links[l.Id].ToList(), l.PanelId, readingOrder, pageNumber, l.NarrationMode);
        }).ToList();

        var bibleIds = await _db.SeriesBibles.AsNoTracking().Where(b => b.SeriesId == chapter.SeriesId)
            .Select(b => b.Id).ToListAsync(ct);
        var bibleEntries = await _db.BibleEntries.AsNoTracking().Where(e => bibleIds.Contains(e.SeriesBibleId))
            .Include(e => e.Revisions).ThenInclude(r => r.Events)
            .ToListAsync(ct);

        var bindings = await _db.ChapterBibleBindings.AsNoTracking()
            .Where(b => b.ChapterId == chapter.Id)
            .ToListAsync(ct);
        var bindingByEntryId = bindings.ToDictionary(b => b.BibleEntryId);

        var chapterInt = (int)Math.Floor(chapter.ChapterNumber);
        var isFrozen = chapter.ScriptStatus == ScriptStatus.Frozen || chapter.IsScriptFrozen;

        var entries = new Dictionary<Guid, CheckEntry>();
        foreach (var e in bibleEntries)
        {
            int? firstEffective;
            int? firstLocked;

            if (bindings.Count > 0)
            {
                if (bindingByEntryId.TryGetValue(e.Id, out var cb))
                {
                    firstEffective = chapterInt;
                    var boundRev = cb.BoundRevisionId.HasValue
                        ? e.Revisions.FirstOrDefault(r => r.Id == cb.BoundRevisionId.Value)
                        : null;
                    firstLocked = (boundRev?.IsLocked == true) ? chapterInt : null;
                }
                else if (!isFrozen)
                {
                    // Trước Gate 1: dùng bản tự gắn (auto binding)
                    var autoRev = e.GetDefaultVariantRevision(canonOnly: false);
                    if (autoRev != null)
                    {
                        firstEffective = chapterInt;
                        firstLocked = autoRev.IsLocked ? chapterInt : null;
                    }
                    else
                    {
                        firstEffective = int.MaxValue;
                        firstLocked = null;
                    }
                }
                else
                {
                    // Chương đã khoá nhưng không có binding cho mục này
                    firstEffective = int.MaxValue;
                    firstLocked = null;
                }
            }
            else
            {
                // Fallback tương thích cho các unit test cũ chưa nạp ChapterBibleBindings
                // Nếu mục Bible có detailsJson chỉ định availableFromChapter/firstEffective thì tôn trọng
                var checkEntryTemp = new CheckEntry(e.Id, e.Name, e.Category, e.IsActive, 1, null, detailsJson: e.DetailsJson);
                firstEffective = checkEntryTemp.AvailableFromChapter ?? 1;
                firstLocked = e.Revisions.Any(r => r.LockedAt != null) ? 1 : null;
            }

            entries[e.Id] = new CheckEntry(
                e.Id, e.Name, e.Category, e.IsActive, firstEffective, firstLocked,
                detailsJson: e.DetailsJson, code: e.Code);
        }

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
        foreach (var f in existing.Where(f => !ConsistencyChecker.IsAiOrVisualFinding(f.RuleCode) && !detectedSet.Contains(f.Fingerprint)))
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
