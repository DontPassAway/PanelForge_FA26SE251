using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.FreezeScript;

/// <summary>
/// D12 / Gate 1: Producer bấm <b>Start Production</b> khi kịch bản đang <c>InReview</c> → <c>Frozen</c>,
/// chương PreProduction → InProduction. Không chặn theo ý kiến Editor (Editor chỉ góp ý), không kiểm Bible Gate
/// <c>@Tag</c> (D11: kịch bản là chữ thuần), không tự dựng trang (khung Trang/Panel đã lưu theo kịch bản).
/// </summary>
public sealed record FreezeChapterScriptCommand(Guid ChapterId, Guid UserId) : IRequest<Result<ChapterDetailDto>>;

public sealed class FreezeChapterScriptCommandHandler : IRequestHandler<FreezeChapterScriptCommand, Result<ChapterDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IChapterCastGate _castGate;
    private readonly IScriptImpactClassifier _impactClassifier;

    public FreezeChapterScriptCommandHandler(IPanelForgeDbContext dbContext, IChapterCastGate castGate)
        : this(dbContext, castGate, new ScriptImpactClassifier())
    {
    }

    public FreezeChapterScriptCommandHandler(
        IPanelForgeDbContext dbContext,
        IChapterCastGate castGate,
        IScriptImpactClassifier impactClassifier)
    {
        _dbContext = dbContext;
        _castGate = castGate;
        _impactClassifier = impactClassifier;
    }

    public async Task<Result<ChapterDetailDto>> Handle(FreezeChapterScriptCommand cmd, CancellationToken ct)
    {
        var chapter = await _dbContext.Chapters
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);

        if (chapter is null)
            return Result<ChapterDetailDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (!chapter.IsContentEditable)
            return Result<ChapterDetailDto>.Failure($"Chương đang ở trạng thái {chapter.Status}, không thể bắt đầu sản xuất.", ResultErrorCodes.Conflict);
        if (chapter.ScriptStatus != ScriptStatus.InReview)
            return Result<ChapterDetailDto>.Failure(
                chapter.ScriptStatus == ScriptStatus.Frozen
                    ? "Kịch bản của chương này đã được chốt trước đó."
                    : "Writer chưa gửi duyệt kịch bản. Chỉ bắt đầu sản xuất được khi kịch bản đang chờ duyệt.",
                ResultErrorCodes.Conflict);

        var snapshot = await ScriptSnapshotBuilder.BuildWithChoicesAsync(_dbContext, chapter.Id, ct);
        if (snapshot.LineCount == 0)
            return Result<ChapterDetailDto>.Failure(
                "Kịch bản chương phải có ít nhất một dòng trước khi bắt đầu sản xuất.", ResultErrorCodes.Validation);

        // Producer chốt đúng bản đã nộp (push); Writer lưu thêm sau lần nộp thì phải nộp lại trước
        var pushed = chapter.LastSubmittedVersionId is { } pushedId
            ? await _dbContext.ScriptVersions.AsNoTracking()
                .Where(v => v.Id == pushedId)
                .Select(v => new { v.Id, v.VersionNumber, v.ContentHash })
                .FirstOrDefaultAsync(ct)
            : null;
        if (pushed is not null && pushed.ContentHash != snapshot.Hash)
            return Result<ChapterDetailDto>.Failure(
                $"Writer đã sửa kịch bản sau lần nộp v{pushed.VersionNumber}. Cần Writer nộp lại để bản chốt sản xuất trùng bản Editor đã đọc.",
                ResultErrorCodes.Conflict);

        // BIB-14: Gate 1 check trước khi StartProduction
        var startGate = await _castGate.EvaluateStartAsync(chapter.Id, ct);
        if (!startGate.CanStart)
        {
            var blockerSummary = string.Join("; ", startGate.Blockers.Select(b => $"{b.EntryName}: {b.Reason} ({b.SourceExplanation ?? "chương"})"));
            return Result<ChapterDetailDto>.Failure(
                $"Không thể bắt đầu sản xuất do Series Bible chưa sẵn sàng: {blockerSummary}",
                ResultErrorCodes.Conflict);
        }

        // Đóng băng lại sau khi mở sửa: gắn cờ ảnh hưởng cho Panel có dòng vừa sửa hoặc đổi binding (WF04 / SCR-01)
        if (chapter.ScriptReopenStatus == ScriptReopenStatus.Approved && chapter.ScriptReopenRequestedAt.HasValue)
            await FlagImpactedWorkAsync(
                chapter.Id,
                chapter.FrozenScriptVersionId,
                new ScriptVersionSnapshot(snapshot.Rows, snapshot.CastChoices),
                ct);

        // Đóng băng is_crowd_at_bind và bound_revision_id
        var chapterBindings = await _dbContext.ChapterBibleBindings
            .Include(b => b.BibleEntry)
            .Where(b => b.ChapterId == chapter.Id)
            .ToListAsync(ct);
        foreach (var b in chapterBindings)
        {
            b.SetCrowdAtBind(b.BibleEntry.IsCrowd);
        }

        // Mã băm dùng chung với phiên bản kịch bản (Trang → Panel → dòng), chốt frozen_script_version_id
        chapter.StartProduction(cmd.UserId, snapshot.Hash, pushed?.Id);
        chapter.UpdatedBy = cmd.UserId.ToString();

        await ScriptNotifications.NotifyWritersAsync(_dbContext, chapter, cmd.UserId,
            $"Kịch bản Chương {chapter.ChapterNumber} đã được chốt",
            $"Producer đã bắt đầu sản xuất Chương {chapter.ChapterNumber}. Kịch bản chuyển sang chỉ đọc.", ct);

        await _dbContext.SaveChangesAsync(ct);
        return Result<ChapterDetailDto>.Success(ChapterDetailDtoFactory.Create(chapter));
    }

    private async Task FlagImpactedWorkAsync(
        Guid chapterId,
        Guid? frozenScriptVersionId,
        ScriptVersionSnapshot currentSnapshot,
        CancellationToken ct)
    {
        if (frozenScriptVersionId is null) return;
        var frozenRowsJson = await _dbContext.ScriptVersions.AsNoTracking()
            .Where(v => v.Id == frozenScriptVersionId.Value && v.ChapterId == chapterId)
            .Select(v => v.RowsJson)
            .FirstOrDefaultAsync(ct);
        if (frozenRowsJson is null) return;

        var impact = _impactClassifier.Classify(
            ScriptSnapshotBuilder.DeserializeSnapshot(frozenRowsJson),
            currentSnapshot);
        if (!impact.HasVisualImpact) return;

        var pageNumbers = impact.VisualPanels.Select(p => p.PageNumber).Distinct().ToList();
        var pages = await _dbContext.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId && pageNumbers.Contains(p.PageNumber))
            .Select(p => new { p.Id, p.PageNumber })
            .ToListAsync(ct);
        var pageNumberById = pages.ToDictionary(p => p.Id, p => p.PageNumber);
        var affectedPageIds = pages.Select(p => p.Id).ToHashSet();

        var affectedPanelIds = impact.VisualPanels.Where(p => p.PanelId.HasValue)
            .Select(p => p.PanelId!.Value)
            .ToHashSet();
        var currentPanels = await _dbContext.Panels.AsNoTracking()
            .Where(p => affectedPageIds.Contains(p.PageId))
            .Select(p => new { p.Id, p.PageId, p.PanelNumber })
            .ToListAsync(ct);
        foreach (var panel in currentPanels.Where(panel => impact.VisualPanels.Any(reference =>
                     reference.PageNumber == pageNumberById[panel.PageId] &&
                     reference.PanelNumber == panel.PanelNumber)))
            affectedPanelIds.Add(panel.Id);

        var actionLines = await _dbContext.ScriptLines.AsNoTracking()
            .Where(l => l.PanelId.HasValue && affectedPanelIds.Contains(l.PanelId.Value) && l.Kind == ScriptLineKind.Action)
            .Select(l => new { l.Id, PanelId = l.PanelId!.Value })
            .ToListAsync(ct);
        var lineByPanel = actionLines.GroupBy(l => l.PanelId).ToDictionary(g => g.Key, g => g.First().Id);

        var artworkStageIds = await _dbContext.PipelineStages.AsNoTracking()
            .Where(s => s.AllowedRole == WorkspaceRole.Artist)
            .Select(s => s.Id)
            .ToListAsync(ct);
        var affectedTargets = affectedPanelIds.Concat(affectedPageIds).ToHashSet();
        var startedAssignmentStatuses = new[]
        {
            AssignmentStatus.InProgress, AssignmentStatus.Submitted, AssignmentStatus.UnderReview,
            AssignmentStatus.Approved, AssignmentStatus.ReworkRequired
        };

        foreach (var a in await _dbContext.Assignments
                     .Where(a => a.ChapterId == chapterId && a.AssignedRole == WorkspaceRole.Artist &&
                                 startedAssignmentStatuses.Contains(a.Status) && affectedTargets.Contains(a.TargetEntityId))
                     .ToListAsync(ct))
        {
            var lineId = lineByPanel.GetValueOrDefault(a.TargetEntityId, actionLines.FirstOrDefault()?.Id ?? Guid.Empty);
            a.FlagScriptImpact(lineId);
        }

        foreach (var sp in await _dbContext.StageProgresses
                      .Where(sp => sp.PanelId.HasValue && affectedPanelIds.Contains(sp.PanelId.Value) &&
                                   artworkStageIds.Contains(sp.PipelineStageId)).ToListAsync(ct))
        {
            var lineId = lineByPanel.GetValueOrDefault(sp.PanelId!.Value, Guid.Empty);
            sp.FlagScriptImpact(lineId);
        }

        // Lưới V3: ô cấp Trang (đã bắt đầu) của trang có dòng vừa sửa mang cờ đầu vào lỗi thời → Impact Review.
        // Không gắn ô cấp Chương: ô Script chính là phần vừa sửa, gắn cờ sẽ khoá mọi ô sau nó.
        foreach (var task in await _dbContext.StageTasks
                     .Where(t => t.ChapterId == chapterId && t.Status != StageTaskStatus.Cancelled &&
                                 artworkStageIds.Contains(t.PipelineStageId) && t.PageId != null &&
                                 affectedPageIds.Contains(t.PageId.Value)).ToListAsync(ct))
            task.MarkInputOutdated(StageTask.ScriptChangedReason);
    }
}

/// <summary>D12: Writer nộp kịch bản (Draft → InReview); kịch bản khoá sửa, báo Editor và Producer.</summary>
public sealed record SubmitScriptCommand(Guid ChapterId, Guid UserId, string? Message = null) : IRequest<Result<ChapterDetailDto>>;

/// <summary>D12: chỉ Writer rút kịch bản về sửa (InReview → Draft). Editor / Producer không đẩy kịch bản về Draft.</summary>
public sealed record RecallScriptCommand(Guid ChapterId, Guid UserId) : IRequest<Result<ChapterDetailDto>>;

public sealed class ScriptLifecycleCommandHandler :
    IRequestHandler<SubmitScriptCommand, Result<ChapterDetailDto>>,
    IRequestHandler<RecallScriptCommand, Result<ChapterDetailDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IChapterCastGate _castGate;
    private readonly IChapterCastService _castService;

    public ScriptLifecycleCommandHandler(
        IPanelForgeDbContext db,
        IWorkspaceAuthorizationService auth,
        IChapterCastGate castGate,
        IChapterCastService castService)
    {
        _db = db;
        _auth = auth;
        _castGate = castGate;
        _castService = castService;
    }

    public async Task<Result<ChapterDetailDto>> Handle(SubmitScriptCommand cmd, CancellationToken ct)
    {
        var loaded = await LoadForWriterAsync(cmd.ChapterId, cmd.UserId, ct);
        if (!loaded.IsSuccess) return Result<ChapterDetailDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);
        var (chapter, workspaceId) = loaded.Value;

        // Gửi duyệt = push: commit bản hiện tại (nếu khác bản gần nhất) rồi đánh dấu đã gửi
        var version = await ScriptSnapshotBuilder.CommitAsync(_db, chapter.Id, cmd.UserId, cmd.Message, ct, _castService);
        if (version.LineCount == 0)
            return Result<ChapterDetailDto>.Failure("Kịch bản còn trống, chưa gửi duyệt được.", ResultErrorCodes.Validation);

        // Cho phép nộp kịch bản trước khi hoàn thành series bible (submitGate ghi nhận thông tin, không chặn)
        var submitGate = await _castGate.EvaluateSubmitAsync(chapter.Id, ct);
        if (!submitGate.CanSubmit)
        {
            var blockerSummary = string.Join("; ", submitGate.Blockers.Select(b => $"{b.EntryName}: {b.Reason}"));
            // Ghi nhận cảnh báo nhưng cho phép nộp kịch bản sang Editor
        }

        if (await ScriptSubmitRules.ApplySubmitAsync(_db, chapter, version, cmd.UserId, workspaceId, ct) is { } submitError)
            return Result<ChapterDetailDto>.Failure(submitError, ResultErrorCodes.Conflict);

        await _db.SaveChangesAsync(ct);
        return Result<ChapterDetailDto>.Success(ChapterDetailDtoFactory.Create(chapter));
    }

    public async Task<Result<ChapterDetailDto>> Handle(RecallScriptCommand cmd, CancellationToken ct)
    {
        var loaded = await LoadForWriterAsync(cmd.ChapterId, cmd.UserId, ct);
        if (!loaded.IsSuccess) return Result<ChapterDetailDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);
        var (chapter, _) = loaded.Value;

        try
        {
            chapter.RecallScript(cmd.UserId);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ChapterDetailDto>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }
        chapter.UpdatedBy = cmd.UserId.ToString();

        await _db.SaveChangesAsync(ct);
        return Result<ChapterDetailDto>.Success(ChapterDetailDtoFactory.Create(chapter));
    }

    /// <summary>Chỉ Writer của Workspace được nộp / rút kịch bản (spec §5.1: Writer độc quyền viết kịch bản).</summary>
    private async Task<Result<(Chapter Chapter, Guid WorkspaceId)>> LoadForWriterAsync(Guid chapterId, Guid userId, CancellationToken ct)
    {
        var chapter = await _db.Chapters
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct);
        if (chapter is null)
            return Result<(Chapter, Guid)>.Failure($"Chương {chapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        if (await _auth.GetMemberRoleAsync(userId, workspaceId, ct) != WorkspaceRole.Writer)
            return Result<(Chapter, Guid)>.Failure("Chỉ Writer mới gửi duyệt hoặc hủy gửi duyệt kịch bản.", ResultErrorCodes.Forbidden);

        return Result<(Chapter, Guid)>.Success((chapter, workspaceId));
    }
}

/// <summary>
/// V2.6 Module 4: Producer mở lại kịch bản (Unfreeze Script); lý do bắt buộc và được ghi Audit Log.
/// </summary>
public sealed record UnfreezeChapterScriptCommand(Guid ChapterId, Guid UserId, string? Reason) : IRequest<Result<ChapterDetailDto>>;

public sealed class UnfreezeChapterScriptCommandHandler : IRequestHandler<UnfreezeChapterScriptCommand, Result<ChapterDetailDto>>
{
    private const int MaxReasonLength = 500;
    private readonly IPanelForgeDbContext _dbContext;

    public UnfreezeChapterScriptCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ChapterDetailDto>> Handle(UnfreezeChapterScriptCommand cmd, CancellationToken ct)
    {
        var chapter = await _dbContext.Chapters
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);

        var reason = cmd.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            return Result<ChapterDetailDto>.Failure("Phải nhập lý do mở lại kịch bản (ghi vào Audit Log).", ResultErrorCodes.Validation);
        if (reason.Length > MaxReasonLength)
            return Result<ChapterDetailDto>.Failure($"Lý do mở lại kịch bản tối đa {MaxReasonLength} ký tự.", ResultErrorCodes.Validation);

        if (chapter is null)
            return Result<ChapterDetailDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (chapter.Status != ChapterStatus.InProduction)
            return Result<ChapterDetailDto>.Failure($"Chỉ mở lại kịch bản được cho chương đang trong sản xuất (InProduction), hiện là {chapter.Status}.", ResultErrorCodes.Conflict);

        if (!chapter.IsScriptFrozen)
            return Result<ChapterDetailDto>.Failure("Kịch bản của chương này chưa bị đóng băng.", ResultErrorCodes.Conflict);

        chapter.UnfreezeScript(cmd.UserId, reason);
        chapter.UpdatedBy = cmd.UserId.ToString();

        await ScriptNotifications.NotifyWritersAsync(_dbContext, chapter, cmd.UserId,
            $"Kịch bản Chương {chapter.ChapterNumber} được mở lại",
            $"Producer đã mở lại kịch bản Chương {chapter.ChapterNumber} để chỉnh sửa. Lý do: {reason}", ct);

        // BR-18: lý do không có cột trên Chapter → ghi bản audit nghiệp vụ trong cùng SaveChanges với thay đổi trạng thái
        var workspaceId = await _dbContext.Series.Where(s => s.Id == chapter.SeriesId).Select(s => (Guid?)s.WorkspaceId).FirstOrDefaultAsync(ct);
        _dbContext.AuditLogs.Add(AuditLog.Create(
            action: "UNFREEZE_SCRIPT",
            entityName: nameof(Chapter),
            entityId: chapter.Id.ToString(),
            workspaceId: workspaceId,
            userId: cmd.UserId,
            details: $"Mở lại kịch bản Chương {chapter.ChapterNumber}. Lý do: {reason}"));

        await _dbContext.SaveChangesAsync(ct);

        return Result<ChapterDetailDto>.Success(ChapterDetailDtoFactory.Create(chapter));
    }
}
