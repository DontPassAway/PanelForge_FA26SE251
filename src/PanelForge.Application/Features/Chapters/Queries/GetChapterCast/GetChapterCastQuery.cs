using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Queries.GetChapterCast;

public sealed record ChapterCastBoundRevisionDto(
    Guid RevisionId,
    int VersionNumber,
    string? VariantLabel,
    BibleRevisionStatus Status,
    bool IsCanonLocked,
    string? ReferenceImageUrl,
    string? Summary
);

public sealed record ChapterCastPanelRevisionDto(
    Guid PanelId,
    int PageNumber,
    int PanelNumber,
    Guid RevisionId,
    int VersionNumber,
    string? VariantLabel,
    BibleRevisionStatus Status,
    bool IsCanonLocked
);

public sealed record ChapterCastAvailableRevisionDto(
    Guid RevisionId,
    int VersionNumber,
    string? VariantLabel,
    BibleRevisionStatus Status,
    bool IsCanonLocked,
    string? ReferenceImageUrl,
    string? Summary
);

public sealed record ChapterCastMentionDto(
    Guid LineId,
    int LineOrder,
    Guid? PanelId,
    int? PageNumber,
    int? PanelNumber,
    string SurfaceText,
    ScriptLineKind Kind
);

public sealed record ChapterCastBlockerDto(
    Guid BibleEntryId,
    string EntryName,
    Guid? RevisionId,
    string Reason,
    string? SourceExplanation = null
);

public sealed record ChapterCastSubmitGateDto(
    bool CanSubmit,
    IReadOnlyList<ChapterCastBlockerDto> Blockers
);

public sealed record ChapterCastStartGateDto(
    bool CanStart,
    IReadOnlyList<ChapterCastBlockerDto> Blockers
);

public sealed record ChapterCastItemDto(
    Guid BibleEntryId,
    string Code,
    string Name,
    BibleEntryCategory Category,
    bool IsStub,
    bool IsCrowd,
    ChapterBibleBindingSource Source,
    string BibleStatus,
    ChapterCastBoundRevisionDto? BoundRevision,
    bool ChosenByWriter,
    decimal? InheritedFromChapterNumber,
    IReadOnlyList<ChapterCastPanelRevisionDto> PanelRevisions,
    IReadOnlyList<ChapterCastAvailableRevisionDto> AvailableRevisions,
    IReadOnlyList<ChapterCastMentionDto> Mentions
);

public sealed record ChapterCastDto(
    Guid ChapterId,
    decimal ChapterNumber,
    string ChapterTitle,
    IReadOnlyList<ChapterCastItemDto> Entities,
    ChapterCastSubmitGateDto SubmitGate,
    ChapterCastStartGateDto Gate
);

public sealed record GetChapterCastQuery(Guid ChapterId, Guid UserId)
    : IRequest<Result<ChapterCastDto>>;

public sealed class GetChapterCastQueryHandler
    : IRequestHandler<GetChapterCastQuery, Result<ChapterCastDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IChapterCastGate _castGate;

    public GetChapterCastQueryHandler(
        IPanelForgeDbContext db,
        IWorkspaceAuthorizationService auth,
        IChapterCastGate castGate)
    {
        _db = db;
        _auth = auth;
        _castGate = castGate;
    }

    public async Task<Result<ChapterCastDto>> Handle(GetChapterCastQuery q, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == q.ChapterId, ct);
        if (chapter is null)
            return Result<ChapterCastDto>.Failure($"Chương {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId)
            .Select(s => s.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        var role = await _auth.GetMemberRoleAsync(q.UserId, workspaceId, ct);
        if (role is null)
            return Result<ChapterCastDto>.Failure("Bạn không phải thành viên của Workspace này.", ResultErrorCodes.Forbidden);

        // Load chapter bindings with entry and bound revision
        var bindings = await _db.ChapterBibleBindings.AsNoTracking()
            .Include(b => b.BibleEntry)
                .ThenInclude(e => e.Revisions)
            .Include(b => b.BoundRevision)
            .Where(b => b.ChapterId == q.ChapterId)
            .ToListAsync(ct);

        // Load pages and panels
        var pages = await _db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == q.ChapterId)
            .OrderBy(p => p.PageNumber)
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

        // Load panel variants for these panels
        var panelVariants = await _db.PanelBibleVariants.AsNoTracking()
            .Include(v => v.Revision)
            .Where(v => v.PanelId.HasValue && panelIds.Contains(v.PanelId.Value))
            .ToListAsync(ct);

        // Load script lines and bible links
        var lines = await _db.ScriptLines.AsNoTracking()
            .Where(l => (l.PanelId != null && l.Panel!.Page.ChapterId == q.ChapterId) || l.Scene.ChapterId == q.ChapterId)
            .OrderBy(l => l.LineOrder)
            .Select(l => new
            {
                l.Id,
                l.LineOrder,
                l.PanelId,
                l.SpeakerLabel,
                l.Kind,
                Text = l.Text ?? l.DialogueText ?? l.StageDirection ?? string.Empty
            })
            .ToListAsync(ct);

        var lineIds = lines.Select(l => l.Id).ToList();
        var links = await _db.ScriptLineBibleLinks.AsNoTracking()
            .Where(k => lineIds.Contains(k.ScriptLineId))
            .ToListAsync(ct);

        // Pre-query previous chapters to determine inheritedFromChapterNumber when not chosenByWriter
        var dbPrevBindings = await _db.ChapterBibleBindings.AsNoTracking()
            .Where(b => b.Chapter.SeriesId == chapter.SeriesId &&
                        b.Chapter.ChapterNumber < chapter.ChapterNumber &&
                        b.BoundRevisionId != null)
            .Select(b => new { b.Chapter.ChapterNumber, b.BibleEntryId, b.BoundRevisionId })
            .ToListAsync(ct);
        var localPrevBindings = _db.ChapterBibleBindings.Local
            .Where(b => b.Chapter != null &&
                        b.Chapter.SeriesId == chapter.SeriesId &&
                        b.Chapter.ChapterNumber < chapter.ChapterNumber &&
                        b.BoundRevisionId != null)
            .Select(b => new { b.Chapter.ChapterNumber, b.BibleEntryId, b.BoundRevisionId })
            .ToList();
        var previousChapterBindings = localPrevBindings.Union(dbPrevBindings).Distinct()
            .OrderByDescending(b => b.ChapterNumber)
            .ToList();

        var entities = new List<ChapterCastItemDto>();

        foreach (var binding in bindings)
        {
            var entry = binding.BibleEntry;
            var isCrowd = binding.IsCrowdAtBind;
            var boundRev = binding.BoundRevision;

            // BibleStatus calculation
            string bibleStatus;
            if (entry.IsStub)
            {
                bibleStatus = "WaitingForDefinition";
            }
            else if (boundRev is null)
            {
                bibleStatus = "WaitingForDefinition";
            }
            else
            {
                bibleStatus = boundRev.ReviewStatus switch
                {
                    BibleRevisionStatus.Draft => "WaitingForArtist",
                    BibleRevisionStatus.ConceptSubmitted => "WaitingForConfirmation",
                    BibleRevisionStatus.ReworkRequired => "ReworkRequired",
                    BibleRevisionStatus.CanonLocked => "CanonLocked",
                    _ => "WaitingForArtist"
                };
            }

            // InheritedFromChapterNumber
            decimal? inheritedChapterNumber = null;
            if (!binding.ChosenByWriter && boundRev is not null)
            {
                var prev = previousChapterBindings
                    .FirstOrDefault(pb => pb.BibleEntryId == entry.Id && pb.BoundRevisionId == boundRev.Id);
                inheritedChapterNumber = prev?.ChapterNumber;
            }

            // BoundRevisionDto
            ChapterCastBoundRevisionDto? boundDto = null;
            if (boundRev is not null)
            {
                boundDto = new ChapterCastBoundRevisionDto(
                    boundRev.Id,
                    boundRev.VersionNumber,
                    boundRev.StageName,
                    boundRev.ReviewStatus,
                    boundRev.IsLocked,
                    boundRev.ConceptImageUrl ?? entry.ReferenceImageUrl,
                    boundRev.Summary);
            }

            // Available revisions
            var available = entry.Revisions
                .Where(r => !r.IsSuperseded)
                .OrderByDescending(r => r.VersionNumber)
                .Select(r => new ChapterCastAvailableRevisionDto(
                    r.Id,
                    r.VersionNumber,
                    r.StageName,
                    r.ReviewStatus,
                    r.IsLocked,
                    r.ConceptImageUrl,
                    r.Summary))
                .ToList();

            // Panel revisions for this entity
            var entityPanelVariants = panelVariants
                .Where(v => v.BibleEntryId == entry.Id)
                .Select(v =>
                {
                    var pan = v.PanelId.HasValue ? panelMap.GetValueOrDefault(v.PanelId.Value) : null;
                    var pageNum = pan != null && pageMap.TryGetValue(pan.PageId, out var p) ? p : 0;
                    var panNum = pan?.PanelNumber ?? 0;
                    return new ChapterCastPanelRevisionDto(
                        v.PanelId!.Value,
                        pageNum,
                        panNum,
                        v.RevisionId,
                        v.Revision.VersionNumber,
                        v.Revision.StageName,
                        v.Revision.ReviewStatus,
                        v.Revision.IsLocked);
                })
                .ToList();

            // Mentions
            var entityMentions = new List<ChapterCastMentionDto>();
            foreach (var l in lines)
            {
                var isSpeaker = !string.IsNullOrWhiteSpace(l.SpeakerLabel) &&
                    entry.NormalizedName == SpeakerResolver.NormalizeSpeaker(l.SpeakerLabel);
                var hasLink = links.FirstOrDefault(k => k.ScriptLineId == l.Id && k.BibleEntryId == entry.Id);
                if (isSpeaker || hasLink != null)
                {
                    var pan = l.PanelId.HasValue ? panelMap.GetValueOrDefault(l.PanelId.Value) : null;
                    var pageNum = pan != null && pageMap.TryGetValue(pan.PageId, out var p) ? (int?)p : null;
                    var panNum = pan?.PanelNumber;
                    var surface = hasLink?.SurfaceText ?? l.SpeakerLabel ?? entry.Name;

                    entityMentions.Add(new ChapterCastMentionDto(
                        l.Id,
                        l.LineOrder,
                        l.PanelId,
                        pageNum,
                        panNum,
                        surface,
                        l.Kind));
                }
            }

            entities.Add(new ChapterCastItemDto(
                entry.Id,
                entry.Code,
                entry.Name,
                entry.Category,
                entry.IsStub,
                isCrowd,
                binding.Source,
                bibleStatus,
                boundDto,
                binding.ChosenByWriter,
                inheritedChapterNumber,
                entityPanelVariants,
                available,
                entityMentions));
        }

        var submitGate = await _castGate.EvaluateSubmitAsync(chapter.Id, ct);
        var startGate = await _castGate.EvaluateStartAsync(chapter.Id, ct);

        return Result<ChapterCastDto>.Success(new ChapterCastDto(
            chapter.Id,
            chapter.ChapterNumber,
            chapter.Title ?? string.Empty,
            entities,
            submitGate,
            startGate));
    }
}
