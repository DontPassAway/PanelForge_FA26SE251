using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Queries.GetChapterReadiness;

public sealed record ReadinessResolutionTargetDto(
    string Workspace,
    string? Section = null,
    string? ResourceId = null
);

public sealed record ReadinessIssueDto(
    string Code,
    string Severity, // "BLOCKING" | "WARNING" | "INFO"
    string Message,
    string? ResourceType = null,
    string? ResourceId = null,
    string? VersionId = null,
    ReadinessResolutionTargetDto? ResolutionTarget = null
);

public sealed record ReadinessCheckDto(
    string Code,
    string Group, // "SCRIPT" | "BIBLE" | "SETUP"
    string Label,
    bool Passed,
    bool Mandatory,
    string StatusText,
    string? Reason = null,
    string? ResourceType = null,
    string? ResourceId = null,
    ReadinessResolutionTargetDto? ResolutionTarget = null
);

public sealed record ReadinessSummaryDto(
    int Required,
    int Passed,
    int Remaining
);

public sealed record ChapterScriptSummaryDto(
    Guid? LatestVersionId,
    int? LatestVersionNumber,
    Guid? ApprovedVersionId,
    int? ApprovedVersionNumber,
    Guid? ProductionVersionId,
    int? ProductionVersionNumber,
    string ScriptStatus,
    bool IsScriptFrozen,
    bool HasUnpushedDraftChanges,
    int SceneCount,
    int PageCount,
    int PanelCount,
    int LineCount,
    int UnresolvedCommentsCount,
    int OpenFindingsCount,
    DateTime? LastUpdatedAt
);

public sealed record BibleCategoryCountDto(
    string Category,
    int Required,
    int Ready,
    int Missing
);

public sealed record ChapterBibleSummaryDto(
    int RequiredTotal,
    int ReadyCount,
    int MissingDefinitionCount,
    int MissingArtCount,
    int PendingApprovalCount,
    int ReworkCount,
    int CrowdOptionalCount,
    int NonVisualReadyCount,
    IReadOnlyList<BibleCategoryCountDto> ByCategory
);

public sealed record StageProgressBreakdownDto(
    Guid StageId,
    string StageName,
    int StageOrder,
    int TotalTasks,
    int ApprovedTasks,
    int SubmittedTasks,
    int InProgressTasks,
    int ReworkTasks,
    int UnassignedTasks
);

public sealed record ChapterProductionProgressDto(
    int TotalTasks,
    int ApprovedTasks,
    int SubmittedTasks,
    int InProgressTasks,
    int ReworkTasks,
    int UnassignedTasks,
    int OverdueTasks,
    int TaskProgressPercent,
    int TotalPages,
    int CompletedPages,
    int PageProgressPercent,
    int TotalPanels,
    int ApprovedPanels,
    int PanelProgressPercent,
    IReadOnlyList<StageProgressBreakdownDto> Stages
);

public sealed record ChapterReadinessDto(
    Guid ChapterId,
    Guid SeriesId,
    decimal ChapterNumber,
    string ChapterTitle,
    string ChapterStatus,
    DateOnly? TargetReleaseDate,
    bool IsHandedOff,
    Guid? EvaluatedScriptVersionId,
    string Status, // "NOT_READY" | "READY" | "STARTED"
    bool CanStartProduction,
    IReadOnlyList<ReadinessCheckDto> Checks,
    IReadOnlyList<ReadinessIssueDto> Blockers,
    IReadOnlyList<ReadinessIssueDto> Warnings,
    ReadinessSummaryDto Summary,
    ChapterScriptSummaryDto Script,
    ChapterBibleSummaryDto Bible,
    ChapterProductionProgressDto Production,
    DateTime EvaluatedAt
);

public sealed record GetChapterReadinessQuery(Guid ChapterId, Guid UserId)
    : IRequest<Result<ChapterReadinessDto>>;

public sealed class GetChapterReadinessQueryHandler
    : IRequestHandler<GetChapterReadinessQuery, Result<ChapterReadinessDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IChapterCastGate _castGate;

    public GetChapterReadinessQueryHandler(
        IPanelForgeDbContext db,
        IWorkspaceAuthorizationService auth,
        IChapterCastGate castGate)
    {
        _db = db;
        _auth = auth;
        _castGate = castGate;
    }

    public async Task<Result<ChapterReadinessDto>> Handle(GetChapterReadinessQuery q, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking()
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == q.ChapterId, ct);
        if (chapter is null)
            return Result<ChapterReadinessDto>.Failure($"Chương {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId)
            .Select(s => s.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        var role = await _auth.GetMemberRoleAsync(q.UserId, workspaceId, ct);
        if (role is null)
            return Result<ChapterReadinessDto>.Failure("Bạn không phải thành viên của Workspace này.", ResultErrorCodes.Forbidden);

        // 1. Authoritative Script Versions (Latest / Approved-Submitted / Production-Frozen)
        var scriptVersions = await _db.ScriptVersions.AsNoTracking()
            .Where(v => v.ChapterId == chapter.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                v.Id,
                v.VersionNumber,
                v.ContentHash,
                v.SubmittedAt,
                v.CreatedAt
            })
            .ToListAsync(ct);

        var latestVersion = scriptVersions.FirstOrDefault();
        var approvedVersion = chapter.LastSubmittedVersionId.HasValue
            ? scriptVersions.FirstOrDefault(v => v.Id == chapter.LastSubmittedVersionId.Value)
            : scriptVersions.FirstOrDefault(v => v.SubmittedAt != null);
        var productionVersion = chapter.FrozenScriptVersionId.HasValue
            ? scriptVersions.FirstOrDefault(v => v.Id == chapter.FrozenScriptVersionId.Value)
            : null;

        var currentSnapshot = await ScriptSnapshotBuilder.BuildWithChoicesAsync(_db, chapter.Id, ct);
        var hasUnpushedDraftChanges = approvedVersion is not null &&
            !string.Equals(approvedVersion.ContentHash, currentSnapshot.Hash, StringComparison.Ordinal);

        var pageIds = chapter.Pages.Select(p => p.Id).ToList();
        var panelCount = pageIds.Count > 0
            ? await _db.Panels.AsNoTracking().CountAsync(p => pageIds.Contains(p.PageId), ct)
            : 0;

        var unresolvedCommentsCount = await _db.ScriptComments.AsNoTracking()
            .CountAsync(c => c.ChapterId == chapter.Id && c.ResolvedAt == null, ct);
        var openFindingsCount = await _db.ConsistencyFindings.AsNoTracking()
            .CountAsync(f => f.ChapterId == chapter.Id && f.Status == ConsistencyFindingStatus.Open, ct);

        // 2. Authoritative Chapter Cast & Bible Gate 1
        var castResult = await new GetChapterCastQueryHandler(_db, _auth, _castGate)
            .Handle(new GetChapterCastQuery(chapter.Id, q.UserId), ct);
        var cast = castResult.IsSuccess ? castResult.Value : null;
        var castEntities = cast?.Entities ?? [];
        var bibleGate = cast?.Gate ?? new ChapterCastStartGateDto(true, []);

        var crowdCount = castEntities.Count(e => e.IsCrowd);
        var requiredEntities = castEntities.Where(e => !e.IsCrowd).ToList();
        var nonVisualReady = requiredEntities.Count(e =>
            e.Category is not (BibleEntryCategory.Character or BibleEntryCategory.Location or BibleEntryCategory.Prop)
            && !e.IsStub);
        var readyCount = requiredEntities.Count(e => e.IsCanonLocked);
        var missingDefCount = requiredEntities.Count(e => e.BibleStatus == "WaitingForDefinition");
        var missingArtCount = requiredEntities.Count(e => e.BibleStatus == "WaitingForArtist");
        var pendingApprovalCount = requiredEntities.Count(e => e.BibleStatus == "WaitingForConfirmation");
        var reworkCount = requiredEntities.Count(e => e.BibleStatus == "ReworkRequired");

        var byCategory = requiredEntities
            .GroupBy(e => e.Category.ToString())
            .Select(g => new BibleCategoryCountDto(
                g.Key,
                g.Count(),
                g.Count(x => x.IsCanonLocked),
                g.Count(x => !x.IsCanonLocked)))
            .OrderBy(x => x.Category)
            .ToList();

        // 3. Pipeline Setup & Production Progress (strictly separated Task / Page / Panel metrics)
        var enabledStages = (await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct))
            .Where(s => s.IsEnabled && !(s.IsTerminal && !s.IsApprovalGate))
            .OrderBy(s => s.StageOrder)
            .ToList();
        var hasArtistStage = enabledStages.Any(s => s.AllowedRole == WorkspaceRole.Artist);

        var stageTasks = await _db.StageTasks.AsNoTracking()
            .Where(t => t.ChapterId == chapter.Id && t.Status != StageTaskStatus.Cancelled)
            .Select(t => new { t.Id, t.PageId, t.PipelineStageId, t.Status, t.LotId })
            .ToListAsync(ct);

        var nowUtc = DateTime.UtcNow;
        var overdueLotIds = (await _db.StageTaskLots.AsNoTracking()
            .Where(l => l.ChapterId == chapter.Id && l.DueDate != null && l.DueDate < nowUtc)
            .Select(l => l.Id)
            .ToListAsync(ct)).ToHashSet();

        var totalTasks = stageTasks.Count;
        var approvedTasks = stageTasks.Count(t => t.Status == StageTaskStatus.Approved);
        var submittedTasks = stageTasks.Count(t => t.Status == StageTaskStatus.Submitted);
        var inProgressTasks = stageTasks.Count(t => t.Status == StageTaskStatus.InProgress);
        var reworkTasks = stageTasks.Count(t => t.Status == StageTaskStatus.ReworkRequired);
        var unassignedTasks = stageTasks.Count(t => t.Status == StageTaskStatus.Unassigned);
        var overdueTasks = stageTasks.Count(t =>
            t.LotId.HasValue &&
            overdueLotIds.Contains(t.LotId.Value) &&
            t.Status != StageTaskStatus.Approved);
        var taskProgressPercent = totalTasks > 0 ? (int)Math.Round(100.0 * approvedTasks / totalTasks) : 0;

        // Page progress: a page is completed when it has >=1 active task and ALL its active tasks are Approved
        var tasksByPage = stageTasks
            .Where(t => t.PageId.HasValue)
            .GroupBy(t => t.PageId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var completedPages = pageIds.Count(pid =>
            tasksByPage.TryGetValue(pid, out var list) &&
            list.Count > 0 &&
            list.All(t => t.Status == StageTaskStatus.Approved));
        var pageProgressPercent = pageIds.Count > 0 ? (int)Math.Round(100.0 * completedPages / pageIds.Count) : 0;

        // Panel progress: from StageProgresses on the final enabled stage (distinct from Task Progress!)
        var finalStageId = enabledStages.LastOrDefault()?.Id;
        var approvedPanels = 0;
        if (finalStageId.HasValue && panelCount > 0)
        {
            var panelIds = await _db.Panels.AsNoTracking()
                .Where(p => pageIds.Contains(p.PageId))
                .Select(p => p.Id)
                .ToListAsync(ct);
            approvedPanels = await _db.StageProgresses.AsNoTracking()
                .CountAsync(sp =>
                    sp.PanelId.HasValue &&
                    panelIds.Contains(sp.PanelId.Value) &&
                    sp.PipelineStageId == finalStageId.Value &&
                    sp.Status == StageProgressStatus.Approved, ct);
        }
        var panelProgressPercent = panelCount > 0 ? (int)Math.Round(100.0 * approvedPanels / panelCount) : 0;

        var stageBreakdown = enabledStages.Select(s =>
        {
            var list = stageTasks.Where(t => t.PipelineStageId == s.Id).ToList();
            return new StageProgressBreakdownDto(
                s.Id,
                s.Name,
                s.StageOrder,
                list.Count,
                list.Count(t => t.Status == StageTaskStatus.Approved),
                list.Count(t => t.Status == StageTaskStatus.Submitted),
                list.Count(t => t.Status == StageTaskStatus.InProgress),
                list.Count(t => t.Status == StageTaskStatus.ReworkRequired),
                list.Count(t => t.Status == StageTaskStatus.Unassigned));
        }).ToList();

        // 4. Evaluate Mandatory Pre-production Checks, Blockers & Warnings
        var isStarted = chapter.Status != ChapterStatus.PreProduction;
        var checks = new List<ReadinessCheckDto>();
        var blockers = new List<ReadinessIssueDto>();
        var warnings = new List<ReadinessIssueDto>();

        var scriptHasContent = currentSnapshot.LineCount > 0;
        var scriptSubmitted = isStarted || chapter.ScriptStatus == ScriptStatus.InReview || chapter.ScriptStatus == ScriptStatus.Frozen;
        var scriptHashSynced = isStarted || !hasUnpushedDraftChanges;

        checks.Add(new ReadinessCheckDto(
            Code: "SCRIPT_SUBMITTED",
            Group: "SCRIPT",
            Label: "Kịch bản đã gửi duyệt",
            Passed: scriptHasContent && scriptSubmitted,
            Mandatory: true,
            StatusText: !scriptHasContent
                ? "Chưa có nội dung"
                : scriptSubmitted
                    ? (approvedVersion != null ? $"Đã nộp v{approvedVersion.VersionNumber}" : "Đã nộp")
                    : "Đang soạn (Draft)",
            Reason: !scriptHasContent
                ? "Kịch bản chưa có dòng nội dung nào."
                : !scriptSubmitted
                    ? "Writer chưa gửi duyệt kịch bản (trạng thái hiện tại là Draft)."
                    : null,
            ResourceType: "script",
            ResourceId: chapter.Id.ToString(),
            ResolutionTarget: new ReadinessResolutionTargetDto("pre-production", "script", chapter.Id.ToString())));

        if (!isStarted)
        {
            if (!scriptHasContent)
            {
                blockers.Add(new ReadinessIssueDto(
                    "SCRIPT_EMPTY",
                    "BLOCKING",
                    "Kịch bản chưa có nội dung. Cần Writer hoàn thiện và gửi duyệt trước khi bắt đầu sản xuất.",
                    "script",
                    chapter.Id.ToString(),
                    latestVersion?.Id.ToString(),
                    new ReadinessResolutionTargetDto("pre-production", "script", chapter.Id.ToString())));
            }
            else if (!scriptSubmitted)
            {
                blockers.Add(new ReadinessIssueDto(
                    "SCRIPT_NOT_SUBMITTED",
                    "BLOCKING",
                    "Kịch bản đang ở trạng thái Draft, chưa được Writer gửi duyệt.",
                    "script",
                    chapter.Id.ToString(),
                    latestVersion?.Id.ToString(),
                    new ReadinessResolutionTargetDto("pre-production", "script", chapter.Id.ToString())));
            }
        }

        checks.Add(new ReadinessCheckDto(
            Code: "SCRIPT_HASH_SYNCED",
            Group: "SCRIPT",
            Label: "Bản gửi duyệt khớp bản nháp mới nhất",
            Passed: scriptHashSynced,
            Mandatory: true,
            StatusText: scriptHashSynced ? "Đồng bộ" : "Có chỉnh sửa chưa nộp",
            Reason: scriptHashSynced
                ? null
                : "Kịch bản đã được chỉnh sửa sau lần gửi duyệt gần nhất. Writer cần gửi duyệt lại để đồng bộ.",
            ResourceType: "script",
            ResourceId: chapter.Id.ToString(),
            ResolutionTarget: new ReadinessResolutionTargetDto("pre-production", "changes", chapter.Id.ToString())));

        if (!isStarted && scriptSubmitted && hasUnpushedDraftChanges)
        {
            blockers.Add(new ReadinessIssueDto(
                "SCRIPT_DRAFT_AHEAD",
                "BLOCKING",
                "Kịch bản có thay đổi mới hơn bản đã gửi duyệt. Cần gửi duyệt bản mới nhất trước khi chốt sản xuất.",
                "script",
                chapter.Id.ToString(),
                latestVersion?.Id.ToString(),
                new ReadinessResolutionTargetDto("pre-production", "changes", chapter.Id.ToString())));
        }

        var bibleReady = bibleGate.CanStart;
        checks.Add(new ReadinessCheckDto(
            Code: "BIBLE_CANON_LOCKED",
            Group: "BIBLE",
            Label: "Tài liệu tham khảo bắt buộc đã khóa chuẩn",
            Passed: bibleReady,
            Mandatory: true,
            StatusText: requiredEntities.Count == 0
                ? "Không có thực thể bắt buộc"
                : $"{readyCount}/{requiredEntities.Count} đã sẵn sàng",
            Reason: bibleReady
                ? null
                : $"Còn {bibleGate.Blockers.Count} tài nguyên chưa khóa chuẩn Canon.",
            ResourceType: "bible",
            ResourceId: chapter.Id.ToString(),
            ResolutionTarget: new ReadinessResolutionTargetDto("pre-production", "entities", chapter.Id.ToString())));

        if (!isStarted && !bibleReady)
        {
            foreach (var b in bibleGate.Blockers)
            {
                blockers.Add(new ReadinessIssueDto(
                    "BIBLE_REFERENCE_BLOCKED",
                    "BLOCKING",
                    $"{b.EntryName}: {b.Reason}",
                    "bible_entry",
                    b.BibleEntryId.ToString(),
                    b.RevisionId?.ToString(),
                    new ReadinessResolutionTargetDto("pre-production", "entities", b.BibleEntryId.ToString())));
            }
        }

        var hasPages = chapter.Pages.Count > 0;
        checks.Add(new ReadinessCheckDto(
            Code: "CHAPTER_PAGES_READY",
            Group: "SETUP",
            Label: "Cấu trúc trang của chương",
            Passed: hasPages,
            Mandatory: true,
            StatusText: hasPages ? $"{chapter.Pages.Count} trang · {panelCount} khung" : "Chưa có trang",
            Reason: hasPages ? null : "Chương chưa có trang nào để khởi tạo bảng sản xuất.",
            ResourceType: "chapter",
            ResourceId: chapter.Id.ToString(),
            ResolutionTarget: new ReadinessResolutionTargetDto("pre-production", "script", chapter.Id.ToString())));

        if (!isStarted && !hasPages)
        {
            blockers.Add(new ReadinessIssueDto(
                "NO_PAGES",
                "BLOCKING",
                "Chương chưa có trang nào để giao sản xuất.",
                "chapter",
                chapter.Id.ToString(),
                null,
                new ReadinessResolutionTargetDto("pre-production", "script", chapter.Id.ToString())));
        }

        checks.Add(new ReadinessCheckDto(
            Code: "PIPELINE_STAGE_READY",
            Group: "SETUP",
            Label: "Quy trình công đoạn (Pipeline)",
            Passed: hasArtistStage,
            Mandatory: true,
            StatusText: hasArtistStage ? $"{enabledStages.Count} công đoạn đang bật" : "Thiếu công đoạn Artist",
            Reason: hasArtistStage ? null : "Pipeline của Series chưa bật công đoạn Artist nào.",
            ResourceType: "pipeline",
            ResourceId: chapter.SeriesId.ToString(),
            ResolutionTarget: new ReadinessResolutionTargetDto("production", "board", chapter.Id.ToString())));

        if (!isStarted && !hasArtistStage)
        {
            blockers.Add(new ReadinessIssueDto(
                "NO_ARTIST_STAGE",
                "BLOCKING",
                "Pipeline của Series chưa có công đoạn Artist đang bật.",
                "pipeline",
                chapter.SeriesId.ToString(),
                null,
                new ReadinessResolutionTargetDto("production", "board", chapter.Id.ToString())));
        }

        // Non-blocking warnings
        if (unresolvedCommentsCount > 0)
        {
            warnings.Add(new ReadinessIssueDto(
                "UNRESOLVED_SCRIPT_COMMENTS",
                "WARNING",
                $"Còn {unresolvedCommentsCount} góp ý kịch bản chưa đánh dấu đã xử lý.",
                "script_comment",
                chapter.Id.ToString(),
                null,
                new ReadinessResolutionTargetDto("pre-production", "comments", chapter.Id.ToString())));
        }

        if (openFindingsCount > 0)
        {
            warnings.Add(new ReadinessIssueDto(
                "OPEN_CONSISTENCY_FINDINGS",
                "WARNING",
                $"Còn {openFindingsCount} cảnh báo nhất quán kịch bản cần xem xét.",
                "consistency_finding",
                chapter.Id.ToString(),
                null,
                new ReadinessResolutionTargetDto("pre-production", "checks", chapter.Id.ToString())));
        }

        if (isStarted && !chapter.IsScriptFrozen)
        {
            warnings.Add(new ReadinessIssueDto(
                "SCRIPT_UNFROZEN_IN_PRODUCTION",
                "WARNING",
                "Kịch bản đang được mở khóa trong khi chương đang sản xuất. Cần khóa lại (Refreeze) sau khi hoàn tất chỉnh sửa.",
                "script",
                chapter.Id.ToString(),
                latestVersion?.Id.ToString(),
                new ReadinessResolutionTargetDto("pre-production", "changes", chapter.Id.ToString())));
        }

        var mandatoryChecks = checks.Where(c => c.Mandatory).ToList();
        var passedMandatory = mandatoryChecks.Count(c => c.Passed);
        var canStartProduction = !isStarted && blockers.Count == 0 && passedMandatory == mandatoryChecks.Count;
        var readinessStatus = isStarted ? "STARTED" : (canStartProduction ? "READY" : "NOT_READY");

        var evaluatedVersionId = chapter.FrozenScriptVersionId ?? approvedVersion?.Id ?? latestVersion?.Id;

        var dto = new ChapterReadinessDto(
            ChapterId: chapter.Id,
            SeriesId: chapter.SeriesId,
            ChapterNumber: chapter.ChapterNumber,
            ChapterTitle: chapter.Title ?? string.Empty,
            ChapterStatus: chapter.Status.ToString(),
            TargetReleaseDate: chapter.TargetReleaseDate,
            IsHandedOff: chapter.IsHandedOff,
            EvaluatedScriptVersionId: evaluatedVersionId,
            Status: readinessStatus,
            CanStartProduction: canStartProduction,
            Checks: checks,
            Blockers: blockers,
            Warnings: warnings,
            Summary: new ReadinessSummaryDto(
                Required: mandatoryChecks.Count,
                Passed: passedMandatory,
                Remaining: mandatoryChecks.Count - passedMandatory),
            Script: new ChapterScriptSummaryDto(
                LatestVersionId: latestVersion?.Id,
                LatestVersionNumber: latestVersion?.VersionNumber,
                ApprovedVersionId: approvedVersion?.Id,
                ApprovedVersionNumber: approvedVersion?.VersionNumber,
                ProductionVersionId: productionVersion?.Id,
                ProductionVersionNumber: productionVersion?.VersionNumber,
                ScriptStatus: chapter.ScriptStatus.ToString(),
                IsScriptFrozen: chapter.IsScriptFrozen,
                HasUnpushedDraftChanges: hasUnpushedDraftChanges,
                SceneCount: chapter.Scenes.Count,
                PageCount: chapter.Pages.Count,
                PanelCount: panelCount,
                LineCount: currentSnapshot.LineCount,
                UnresolvedCommentsCount: unresolvedCommentsCount,
                OpenFindingsCount: openFindingsCount,
                LastUpdatedAt: latestVersion?.CreatedAt ?? chapter.UpdatedAt ?? chapter.CreatedAt),
            Bible: new ChapterBibleSummaryDto(
                RequiredTotal: requiredEntities.Count,
                ReadyCount: readyCount,
                MissingDefinitionCount: missingDefCount,
                MissingArtCount: missingArtCount,
                PendingApprovalCount: pendingApprovalCount,
                ReworkCount: reworkCount,
                CrowdOptionalCount: crowdCount,
                NonVisualReadyCount: nonVisualReady,
                ByCategory: byCategory),
            Production: new ChapterProductionProgressDto(
                TotalTasks: totalTasks,
                ApprovedTasks: approvedTasks,
                SubmittedTasks: submittedTasks,
                InProgressTasks: inProgressTasks,
                ReworkTasks: reworkTasks,
                UnassignedTasks: unassignedTasks,
                OverdueTasks: overdueTasks,
                TaskProgressPercent: taskProgressPercent,
                TotalPages: pageIds.Count,
                CompletedPages: completedPages,
                PageProgressPercent: pageProgressPercent,
                TotalPanels: panelCount,
                ApprovedPanels: approvedPanels,
                PanelProgressPercent: panelProgressPercent,
                Stages: stageBreakdown),
            EvaluatedAt: DateTime.UtcNow
        );

        return Result<ChapterReadinessDto>.Success(dto);
    }
}
