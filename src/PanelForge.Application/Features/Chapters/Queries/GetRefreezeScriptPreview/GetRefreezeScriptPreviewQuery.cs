using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Queries.GetRefreezeScriptPreview;

public sealed record GetRefreezeScriptPreviewQuery(Guid ChapterId, Guid UserId)
    : IRequest<Result<RefreezeScriptPreviewDto>>;

public sealed class GetRefreezeScriptPreviewQueryHandler
    : IRequestHandler<GetRefreezeScriptPreviewQuery, Result<RefreezeScriptPreviewDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pageRepository;
    private readonly IChapterCastGate _castGate;
    private readonly IWorkspaceAuthorizationService _auth;

    public GetRefreezeScriptPreviewQueryHandler(
        IPanelForgeDbContext db,
        IPageRepository pageRepository,
        IChapterCastGate castGate,
        IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _pageRepository = pageRepository;
        _castGate = castGate;
        _auth = auth;
    }

    public async Task<Result<RefreezeScriptPreviewDto>> Handle(GetRefreezeScriptPreviewQuery q, CancellationToken ct)
    {
        static Result<RefreezeScriptPreviewDto> Fail(string msg, string code) =>
            Result<RefreezeScriptPreviewDto>.Failure(msg, code);

        var chapter = await _db.Chapters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == q.ChapterId, ct);
        if (chapter is null)
            return Fail($"Chương {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId)
            .Select(s => s.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        var isOwner = await _db.StudioWorkspaces.AsNoTracking()
            .AnyAsync(w => w.Id == workspaceId && w.OwnerId == q.UserId, ct);
        var role = await _auth.GetMemberRoleAsync(q.UserId, workspaceId, ct);
        if (!isOwner && role != WorkspaceRole.Producer)
            return Fail("Chỉ Producer mới có quyền xem trước chốt lại kịch bản.", ResultErrorCodes.Forbidden);

        if (chapter.Status != ChapterStatus.InProduction)
            return Fail(
                $"Chỉ xem trước chốt lại kịch bản khi chương đang trong sản xuất (InProduction), hiện là {chapter.Status}.",
                ResultErrorCodes.Conflict);

        if (chapter.ScriptStatus != ScriptStatus.InReview)
            return Fail(
                "Biên kịch chưa nộp bản sửa kịch bản (ScriptStatus phải là InReview).",
                ResultErrorCodes.Conflict);

        var currentSnapshot = await ScriptSnapshotBuilder.BuildWithChoicesAsync(_db, chapter.Id, ct);

        var latestVersion = await _db.ScriptVersions.AsNoTracking()
            .Where(v => v.ChapterId == chapter.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new { v.Id, v.VersionNumber, v.ContentHash })
            .FirstOrDefaultAsync(ct);

        var submittedVersion = chapter.LastSubmittedVersionId is { } subId
            ? await _db.ScriptVersions.AsNoTracking()
                .Where(v => v.Id == subId)
                .FirstOrDefaultAsync(ct)
            : null;

        var oldFrozenVersion = chapter.FrozenScriptVersionId is { } oldId
            ? await _db.ScriptVersions.AsNoTracking()
                .Where(v => v.Id == oldId)
                .FirstOrDefaultAsync(ct)
            : null;

        var oldSnapshot = oldFrozenVersion is not null
            ? ScriptSnapshotBuilder.DeserializeSnapshot(oldFrozenVersion.RowsJson)
            : new ScriptVersionSnapshot([]);

        var newSnapshot = submittedVersion is not null
            ? ScriptSnapshotBuilder.DeserializeSnapshot(submittedVersion.RowsJson)
            : new ScriptVersionSnapshot(currentSnapshot.Rows, currentSnapshot.CastChoices);

        var diff = await ScriptSnapshotDiffer.ComputeAsync(_db, _pageRepository, chapter, oldSnapshot, newSnapshot, ct);
        var bibleGate = await _castGate.EvaluateStartAsync(chapter.Id, ct);

        var isHashMatched = submittedVersion is not null &&
                            string.Equals(submittedVersion.ContentHash, currentSnapshot.Hash, StringComparison.Ordinal);

        return Result<RefreezeScriptPreviewDto>.Success(new RefreezeScriptPreviewDto(
            ChapterId: chapter.Id,
            ChapterNumber: chapter.ChapterNumber,
            ChapterTitle: chapter.Title,
            PreviousFrozenVersionId: oldFrozenVersion?.Id,
            PreviousFrozenVersionNumber: oldFrozenVersion?.VersionNumber,
            SubmittedVersionId: submittedVersion?.Id,
            SubmittedVersionNumber: submittedVersion?.VersionNumber,
            HashParity: new RefreezeHashParityDto(
                IsMatched: isHashMatched,
                CurrentDraftHash: currentSnapshot.Hash,
                SubmittedHash: submittedVersion?.ContentHash,
                SubmittedVersionNumber: submittedVersion?.VersionNumber,
                LatestVersionNumber: latestVersion?.VersionNumber),
            BibleGate: new RefreezeBibleGateDto(
                IsCanonLocked: bibleGate.CanStart,
                Blockers: bibleGate.Blockers),
            AddedPages: diff.AddedPages,
            RemovedPages: diff.RemovedPages,
            ImpactSummary: new RefreezeImpactSummaryDto(
                VisualImpactCount: diff.VisualImpacts.Count,
                TextImpactCount: diff.TextImpacts.Count),
            VisualImpacts: diff.VisualImpacts,
            TextImpacts: diff.TextImpacts));
    }
}
