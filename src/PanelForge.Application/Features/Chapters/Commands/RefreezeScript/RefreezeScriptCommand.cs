using System.Text.Json;
using PanelForge.Application.Features.SeriesMembers.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Application.Features.TaskGrid.Commands.AssignStageTaskLots;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.RefreezeScript;

public sealed record RefreezeScriptCommand(
    Guid ChapterId,
    Guid UserId,
    string ExpectedScriptHash,
    IReadOnlyList<NewPageAssignmentRequest> NewPageAssignments)
    : IRequest<Result<RefreezeScriptResultDto>>;

public sealed class RefreezeScriptCommandHandler
    : IRequestHandler<RefreezeScriptCommand, Result<RefreezeScriptResultDto>>
{
    private static readonly StageTaskStatus[] NotStartedStatuses =
        [StageTaskStatus.Cancelled, StageTaskStatus.Unassigned, StageTaskStatus.Assigned];

    private readonly IPanelForgeDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPageRepository _pageRepository;
    private readonly IChapterCastGate _castGate;
    private readonly IWorkspaceAuthorizationService _auth;

    public RefreezeScriptCommandHandler(
        IPanelForgeDbContext db,
        IUnitOfWork unitOfWork,
        IPageRepository pageRepository,
        IChapterCastGate castGate,
        IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _pageRepository = pageRepository;
        _castGate = castGate;
        _auth = auth;
    }

    public async Task<Result<RefreezeScriptResultDto>> Handle(RefreezeScriptCommand cmd, CancellationToken ct)
    {
        static Result<RefreezeScriptResultDto> Fail(string msg, string code, object? details = null) =>
            Result<RefreezeScriptResultDto>.Failure(msg, code, details);

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async tct =>
            {
                await _db.LockChapterAsync(cmd.ChapterId, tct);

                var chapter = await _db.Chapters
                    .Include(c => c.Scenes)
                    .Include(c => c.Pages)
                    .FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, tct);
                if (chapter is null)
                    return Fail($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

                var workspaceId = await _db.Series.AsNoTracking()
                    .Where(s => s.Id == chapter.SeriesId)
                    .Select(s => s.WorkspaceId)
                    .SingleAsync(tct);

                var isOwner = await _db.StudioWorkspaces.AsNoTracking()
                    .AnyAsync(w => w.Id == workspaceId && w.OwnerId == cmd.UserId, tct);
                var actorRole = await _auth.GetMemberRoleAsync(cmd.UserId, workspaceId, tct);
                if (!isOwner && actorRole != WorkspaceRole.Producer)
                    return Fail("Chỉ Producer mới có quyền chốt lại kịch bản.", ResultErrorCodes.Forbidden);

                if (chapter.Status != ChapterStatus.InProduction)
                    return Fail(
                        "Chỉ chốt lại kịch bản cho chương đang ở trạng thái InProduction.",
                        ResultErrorCodes.Conflict);

                if (chapter.ScriptStatus != ScriptStatus.InReview)
                    return Fail(
                        "Biên kịch chưa nộp bản sửa kịch bản để duyệt (ScriptStatus phải là InReview).",
                        ResultErrorCodes.Conflict);

                var activePages = await _db.Pages
                    .Where(p => p.ChapterId == chapter.Id)
                    .OrderBy(p => p.PageNumber)
                    .ToListAsync(tct);
                if (activePages.Count == 0)
                    return Fail("Kịch bản chương phải có ít nhất một trang.", ResultErrorCodes.Validation);

                var currentSnapshot = await ScriptSnapshotBuilder.BuildWithChoicesAsync(_db, chapter.Id, tct);
                if (currentSnapshot.LineCount == 0)
                    return Fail("Kịch bản chương phải có ít nhất một dòng.", ResultErrorCodes.Validation);

                var submittedVersion = chapter.LastSubmittedVersionId is { } subId
                    ? await _db.ScriptVersions.AsNoTracking()
                        .FirstOrDefaultAsync(v => v.Id == subId, tct)
                    : null;
                if (submittedVersion is null)
                    return Fail("Chưa tìm thấy phiên bản kịch bản đã nộp để chốt.", ResultErrorCodes.Conflict);

                if (!string.Equals(submittedVersion.ContentHash, currentSnapshot.Hash, StringComparison.Ordinal) ||
                    !string.Equals(submittedVersion.ContentHash, cmd.ExpectedScriptHash, StringComparison.Ordinal))
                {
                    return Fail(
                        $"Biên kịch đã lưu bản nháp mới hoặc dữ liệu chốt lại không khớp với bản v{submittedVersion.VersionNumber}. Vui lòng yêu cầu Biên kịch nộp lại kịch bản.",
                        ResultErrorCodes.HashMismatch,
                        new
                        {
                            submittedHash = submittedVersion.ContentHash,
                            currentDraftHash = currentSnapshot.Hash,
                            submittedVersionNumber = submittedVersion.VersionNumber
                        });
                }

                var bibleGate = await _castGate.EvaluateStartAsync(chapter.Id, tct);
                if (!bibleGate.CanStart)
                {
                    return Fail(
                        "Không thể chốt lại kịch bản do Series Bible chưa sẵn sàng.",
                        ResultErrorCodes.BibleGateBlocked,
                        new { blockers = bibleGate.Blockers });
                }

                var oldFrozenVersion = chapter.FrozenScriptVersionId is { } oldId
                    ? await _db.ScriptVersions.AsNoTracking()
                        .FirstOrDefaultAsync(v => v.Id == oldId, tct)
                    : null;

                var oldSnapshot = oldFrozenVersion is not null
                    ? ScriptSnapshotBuilder.DeserializeSnapshot(oldFrozenVersion.RowsJson)
                    : new ScriptVersionSnapshot([]);
                var newSnapshot = ScriptSnapshotBuilder.DeserializeSnapshot(submittedVersion.RowsJson);

                var diff = await ScriptSnapshotDiffer.ComputeAsync(_db, _pageRepository, chapter, oldSnapshot, newSnapshot, tct);

                // 1. RemovedPages Race-Condition Guard + Cancellation
                var cancelledTasksCount = 0;
                if (diff.RemovedPages.Count > 0)
                {
                    var removedPageIds = diff.RemovedPages.Select(p => p.PageId).ToList();
                    var blockedRemovedIds = new HashSet<Guid>();

                    var freshArtworkIds = await _db.AssetReferences.AsNoTracking()
                        .Where(r => removedPageIds.Contains(r.PageId))
                        .Select(r => r.PageId)
                        .Distinct()
                        .ToListAsync(tct);
                    foreach (var pid in freshArtworkIds) blockedRemovedIds.Add(pid);

                    var removedPageTasks = await _db.StageTasks
                        .Where(t => t.ChapterId == chapter.Id && t.PageId != null &&
                                    removedPageIds.Contains(t.PageId.Value) &&
                                    t.Status != StageTaskStatus.Cancelled)
                        .ToListAsync(tct);

                    foreach (var pid in removedPageTasks.Where(t => !NotStartedStatuses.Contains(t.Status)).Select(t => t.PageId!.Value))
                        blockedRemovedIds.Add(pid);

                    foreach (var pid in removedPageIds)
                    {
                        var pageStream = await _pageRepository.GetAsync(pid, tct);
                        if (pageStream is not null && pageStream.Elements.Values.Any(e =>
                                !e.IsRemoved && (e.ScriptLineId.HasValue || Page.TextElementTypes.Contains(e.ElementType) || !string.IsNullOrEmpty(e.AssetId))))
                        {
                            blockedRemovedIds.Add(pid);
                        }
                    }

                    if (blockedRemovedIds.Count > 0)
                    {
                        var blockedNums = diff.RemovedPages
                            .Where(p => blockedRemovedIds.Contains(p.PageId))
                            .Select(p => p.PageNumber)
                            .OrderBy(n => n)
                            .ToList();
                        throw new RefreezeAbortException(
                            $"Không thể chốt kịch bản vì Trang {string.Join(", ", blockedNums)} vừa có bản vẽ hoặc bóng thoại mới được tạo.",
                            ResultErrorCodes.ActiveArtworkOnRemovedPage,
                            new { blockedPageIds = blockedRemovedIds.ToList() });
                    }

                    var lotIds = removedPageTasks.Select(t => t.LotId).ToList();
                    foreach (var task in removedPageTasks)
                    {
                        if (task.Status == StageTaskStatus.Assigned) task.Unassign();
                        task.Cancel();
                        cancelledTasksCount++;
                    }
                    await TaskGridRules.RefreshLotsAsync(_db, lotIds, removedPageTasks, tct);

                    var legacyAssignments = await _db.Assignments
                        .Where(a => a.ChapterId == chapter.Id && a.EntityType != WorkflowEntityType.Chapter &&
                                    removedPageIds.Contains(a.TargetEntityId))
                        .ToListAsync(tct);
                    foreach (var a in legacyAssignments)
                    {
                        a.Cancel("Trang đã được gỡ bỏ khi chốt lại kịch bản.");
                        _db.Assignments.Remove(a);
                    }
                }

                // 2. AddedPages Validation, StageTask Generation & Lot Assignment (strictly for addedPageIds)
                var addedAssignmentsByPage = (cmd.NewPageAssignments ?? [])
                    .GroupBy(a => a.PageNumber)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var newlyAssignedUserIds = new HashSet<Guid>();
                var stages = (await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, tct))
                    .Where(s => s.IsEnabled && !(s.IsTerminal && !s.IsApprovalGate))
                    .OrderBy(s => s.StageOrder)
                    .ToList();
                var artistStages = stages.Where(s => s.AllowedRole == WorkspaceRole.Artist).ToList();
                var lettererStage = stages.FirstOrDefault(s => s.AllowedRole == WorkspaceRole.Letterer);

                if (diff.AddedPages.Count > 0)
                {
                    if (stages.Count == 0)
                        return Fail("Series chưa có công đoạn nào đang bật.", ResultErrorCodes.Conflict);

                    foreach (var ap in diff.AddedPages)
                    {
                        if (!addedAssignmentsByPage.TryGetValue(ap.PageNumber, out var entries) || entries.Count != 1)
                            return Fail($"Trang mới {ap.PageNumber} phải được giao đúng 1 Họa sĩ phụ trách.", ResultErrorCodes.Validation);
                        if (entries[0].ArtistId == Guid.Empty)
                            return Fail($"Người nhận trang mới {ap.PageNumber} không hợp lệ.", ResultErrorCodes.Validation);
                    }

                    var requestedMembers = (cmd.NewPageAssignments ?? [])
                        .SelectMany(a => a.LettererId.HasValue ? new[] { a.ArtistId, a.LettererId.Value } : new[] { a.ArtistId })
                        .Distinct()
                        .ToList();
                    // D10: vai trò người nhận lấy theo Series
                    var memberRoles = await SeriesRoles.AssignableRoleMapAsync(_db, workspaceId, chapter.SeriesId, tct);

                    foreach (var ap in diff.AddedPages)
                    {
                        var req = addedAssignmentsByPage[ap.PageNumber][0];
                        if (!memberRoles.TryGetValue(req.ArtistId, out var artistRole))
                            return Fail($"Họa sĩ được chọn cho trang {ap.PageNumber} không phải thành viên đang hoạt động của Workspace.", ResultErrorCodes.Validation);
                        if (artistStages.Count > 0 && !artistStages[0].CanBeHeldBy(artistRole))
                            return Fail($"Trang {ap.PageNumber} cần vai trò Artist cho công đoạn vẽ, người nhận đang là {artistRole}.", ResultErrorCodes.Validation);
                        if (req.LettererId.HasValue && lettererStage is not null)
                        {
                            if (!memberRoles.TryGetValue(req.LettererId.Value, out var letRole))
                                return Fail($"Letterer được chọn cho trang {ap.PageNumber} không phải thành viên đang hoạt động.", ResultErrorCodes.Validation);
                            if (!lettererStage.CanBeHeldBy(letRole))
                                return Fail($"Trang {ap.PageNumber} cần vai trò phù hợp cho công đoạn Lettering.", ResultErrorCodes.Validation);
                        }
                    }

                    var addedPageIds = diff.AddedPages.Select(p => p.PageId).ToList();
                    var existingTasksForAdded = await _db.StageTasks
                        .Where(t => t.ChapterId == chapter.Id && t.PageId != null && addedPageIds.Contains(t.PageId.Value))
                        .ToListAsync(tct);

                    var pageStages = stages.Where(s => s.AssignmentGranularity != WorkflowEntityType.Chapter).ToList();
                    var createdForAdded = new List<StageTask>();
                    foreach (var stage in pageStages)
                    {
                        foreach (var addedPageId in addedPageIds)
                        {
                            var found = existingTasksForAdded.FirstOrDefault(t => t.PipelineStageId == stage.Id && t.PageId == addedPageId);
                            if (found is not null)
                            {
                                if (found.Status == StageTaskStatus.Cancelled) found.Revive();
                                createdForAdded.Add(found);
                            }
                            else
                            {
                                var task = StageTask.Create(chapter.SeriesId, chapter.Id, stage.Id, addedPageId);
                                task.CreatedBy = cmd.UserId.ToString();
                                _db.StageTasks.Add(task);
                                createdForAdded.Add(task);
                            }
                        }
                    }

                    // Flush generated cells for new pages before AssignStageTaskLots
                    await _db.SaveChangesAsync(tct);

                    var lotRequests = new List<StageTaskLotRequest>();
                    foreach (var stage in artistStages)
                    {
                        var byArtist = diff.AddedPages
                            .GroupBy(ap => addedAssignmentsByPage[ap.PageNumber][0].ArtistId);
                        foreach (var grp in byArtist)
                        {
                            var pageIdsSet = grp.Select(g => g.PageId).ToHashSet();
                            var taskIds = createdForAdded
                                .Where(t => t.PipelineStageId == stage.Id && t.PageId.HasValue && pageIdsSet.Contains(t.PageId.Value) && t.Status == StageTaskStatus.Unassigned)
                                .Select(t => t.Id)
                                .ToList();
                            if (taskIds.Count > 0)
                            {
                                var pagesText = string.Join(", ", grp.Select(g => g.PageNumber).OrderBy(n => n));
                                lotRequests.Add(new StageTaskLotRequest(stage.Id, grp.Key, taskIds, $"Trang bổ sung {pagesText}", null));
                                newlyAssignedUserIds.Add(grp.Key);
                            }
                        }
                    }

                    if (lettererStage is not null)
                    {
                        var byLetterer = diff.AddedPages
                            .GroupBy(ap =>
                            {
                                var req = addedAssignmentsByPage[ap.PageNumber][0];
                                return req.LettererId ?? req.ArtistId;
                            });
                        foreach (var grp in byLetterer)
                        {
                            var pageIdsSet = grp.Select(g => g.PageId).ToHashSet();
                            var taskIds = createdForAdded
                                .Where(t => t.PipelineStageId == lettererStage.Id && t.PageId.HasValue && pageIdsSet.Contains(t.PageId.Value) && t.Status == StageTaskStatus.Unassigned)
                                .Select(t => t.Id)
                                .ToList();
                            if (taskIds.Count > 0)
                            {
                                var pagesText = string.Join(", ", grp.Select(g => g.PageNumber).OrderBy(n => n));
                                lotRequests.Add(new StageTaskLotRequest(lettererStage.Id, grp.Key, taskIds, $"Chữ trang bổ sung {pagesText}", null));
                                newlyAssignedUserIds.Add(grp.Key);
                            }
                        }
                    }

                    if (lotRequests.Count > 0)
                    {
                        var assignResult = await new AssignStageTaskLotsCommandHandler(_db)
                            .Handle(new AssignStageTaskLotsCommand(chapter.Id, cmd.UserId, lotRequests, DryRun: false), tct);
                        if (!assignResult.IsSuccess)
                            throw new RefreezeAbortException(assignResult.ErrorMessage!, assignResult.ErrorCode ?? ResultErrorCodes.Conflict);
                    }
                }

                // 3. Apply Split Visual vs. Text Impacts
                if (diff.VisuallyImpactedPageIds.Count > 0)
                {
                    var stageRoleById = stages.ToDictionary(s => s.Id, s => s.AllowedRole);
                    var artistTasksOnImpactedPages = await _db.StageTasks
                        .Where(t => t.ChapterId == chapter.Id && t.PageId != null &&
                                    diff.VisuallyImpactedPageIds.Contains(t.PageId.Value) &&
                                    t.Status != StageTaskStatus.Cancelled)
                        .ToListAsync(tct);

                    foreach (var task in artistTasksOnImpactedPages)
                    {
                        if (stageRoleById.TryGetValue(task.PipelineStageId, out var role) && role == WorkspaceRole.Letterer)
                            continue;
                        task.MarkInputOutdated(StageTask.ScriptChangedReason);
                    }

                    var targetEntityIds = diff.VisuallyImpactedPanelIds
                        .Union(diff.VisuallyImpactedPageIds)
                        .ToHashSet();

                    foreach (var a in await _db.Assignments
                        .Where(a => a.ChapterId == chapter.Id && targetEntityIds.Contains(a.TargetEntityId))
                        .ToListAsync(tct))
                    {
                        var lineId = diff.VisualImpactLineByPanel.GetValueOrDefault(a.TargetEntityId, Guid.Empty);
                        a.FlagScriptImpact(lineId);
                        a.FlagImpactReview("Nội dung hình ảnh hoặc Series Bible đã thay đổi khi chốt lại kịch bản (⚑).");
                    }

                    if (diff.VisuallyImpactedPanelIds.Count > 0)
                    {
                        foreach (var sp in await _db.StageProgresses
                            .Where(sp => sp.PanelId.HasValue && diff.VisuallyImpactedPanelIds.Contains(sp.PanelId.Value))
                            .ToListAsync(tct))
                        {
                            var lineId = diff.VisualImpactLineByPanel.GetValueOrDefault(sp.PanelId!.Value, Guid.Empty);
                            sp.FlagScriptImpact(lineId);
                        }
                    }
                }

                // 4. Freeze Bible Bindings & Transition Chapter State
                var chapterBindings = await _db.ChapterBibleBindings
                    .Include(b => b.BibleEntry)
                    .Where(b => b.ChapterId == chapter.Id)
                    .ToListAsync(tct);
                foreach (var b in chapterBindings)
                    b.SetCrowdAtBind(b.BibleEntry.IsCrowd);

                var previousFrozenId = chapter.FrozenScriptVersionId;
                chapter.RefreezeScript(cmd.UserId, currentSnapshot.Hash, submittedVersion.Id);
                chapter.UpdatedBy = cmd.UserId.ToString();

                // 5. Clear working copy in same transaction
                var workingCopy = await _db.ChapterScriptWorkingCopies
                    .FirstOrDefaultAsync(w => w.ChapterId == chapter.Id, tct);
                if (workingCopy is not null)
                    _db.ChapterScriptWorkingCopies.Remove(workingCopy);

                // 6. Audit Log
                _db.AuditLogs.Add(AuditLog.Create(
                    action: "SCRIPT_REFROZEN",
                    entityName: nameof(Chapter),
                    entityId: chapter.Id.ToString(),
                    workspaceId: workspaceId,
                    userId: cmd.UserId,
                    changesJson: JsonSerializer.Serialize(new
                    {
                        PreviousFrozenScriptVersionId = previousFrozenId,
                        FrozenScriptVersionId = submittedVersion.Id,
                        AddedPagesCount = diff.AddedPages.Count,
                        RemovedPagesCount = diff.RemovedPages.Count,
                        CancelledTasksCount = cancelledTasksCount,
                        VisualImpactCount = diff.VisualImpacts.Count,
                        TextImpactCount = diff.TextImpacts.Count
                    }),
                    details: $"Chốt lại kịch bản Chương {chapter.ChapterNumber} (v{submittedVersion.VersionNumber})."));

                // 7. Targeted Notifications
                foreach (var artistId in diff.VisualImpactArtistUserIds.Where(id => id != cmd.UserId))
                {
                    _db.Notifications.Add(Notification.Create(
                        artistId,
                        NotificationType.AssignmentUpdated,
                        $"Kịch bản Chương {chapter.ChapterNumber} có thay đổi hình ảnh",
                        $"Một số khung tranh bạn đang vẽ có chỉnh sửa nội dung hoặc thiết kế nhân vật ({diff.VisualImpacts.Count} thay đổi).",
                        chapterId: chapter.Id));
                }

                foreach (var lettererId in diff.TextImpactLettererUserIds.Where(id => id != cmd.UserId))
                {
                    _db.Notifications.Add(Notification.Create(
                        lettererId,
                        NotificationType.AssignmentUpdated,
                        $"Lời thoại Chương {chapter.ChapterNumber} đã cập nhật",
                        $"Có {diff.TextImpacts.Count} dòng thoại/dẫn truyện thay đổi. Vui lòng kiểm tra các bóng thoại cần cập nhật.",
                        chapterId: chapter.Id));
                }

                foreach (var newAssigneeId in newlyAssignedUserIds.Where(id => id != cmd.UserId))
                {
                    _db.Notifications.Add(Notification.Create(
                        newAssigneeId,
                        NotificationType.AssignmentAssigned,
                        $"Giao trang bổ sung — Chương {chapter.ChapterNumber}",
                        $"Producer đã giao trang mới bổ sung của Chương {chapter.ChapterNumber} cho bạn.",
                        chapterId: chapter.Id));
                }

                await ScriptNotifications.NotifyWritersAsync(
                    _db, chapter, cmd.UserId,
                    $"Kịch bản Chương {chapter.ChapterNumber} đã được chốt lại",
                    $"Producer đã khóa lại kịch bản Chương {chapter.ChapterNumber} (v{submittedVersion.VersionNumber}).",
                    tct);

                await _db.SaveChangesAsync(tct);

                return Result<RefreezeScriptResultDto>.Success(new RefreezeScriptResultDto(
                    ChapterId: chapter.Id,
                    FrozenScriptVersionId: submittedVersion.Id,
                    PreviousFrozenScriptVersionId: previousFrozenId,
                    AddedPagesCount: diff.AddedPages.Count,
                    CancelledTasksCount: cancelledTasksCount,
                    VisualImpactCount: diff.VisualImpacts.Count,
                    TextImpactCount: diff.TextImpacts.Count,
                    Message: $"Kịch bản Chương {chapter.ChapterNumber} đã được chốt lại thành công."));
            }, ct);
        }
        catch (RefreezeAbortException ex)
        {
            return Fail(ex.Message, ex.Code, ex.Details);
        }
        catch (DbUpdateException)
        {
            return Fail(
                "Không thể chốt lại kịch bản vì dữ liệu vừa thay đổi. Vui lòng tải lại và thử lại.",
                ResultErrorCodes.Conflict);
        }
    }

    private sealed class RefreezeAbortException(string message, string code, object? details = null) : Exception(message)
    {
        public string Code { get; } = code;
        public object? Details { get; } = details;
    }
}
