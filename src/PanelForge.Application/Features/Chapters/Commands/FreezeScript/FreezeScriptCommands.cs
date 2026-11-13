using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
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

    public FreezeChapterScriptCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
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

        var snapshot = await ScriptSnapshotBuilder.BuildAsync(_dbContext, chapter.Id, ct);
        if (snapshot.LineCount == 0)
            return Result<ChapterDetailDto>.Failure(
                "Kịch bản chương phải có ít nhất một dòng trước khi bắt đầu sản xuất.", ResultErrorCodes.Validation);

        // Producer chốt đúng bản đã nộp (push); Writer lưu thêm sau lần nộp thì phải nộp lại trước
        var pushed = chapter.LastSubmittedVersionId is { } pushedId
            ? await _dbContext.ScriptVersions.AsNoTracking()
                .Where(v => v.Id == pushedId)
                .Select(v => new { v.VersionNumber, v.ContentHash })
                .FirstOrDefaultAsync(ct)
            : null;
        if (pushed is not null && pushed.ContentHash != snapshot.Hash)
            return Result<ChapterDetailDto>.Failure(
                $"Writer đã sửa kịch bản sau lần nộp v{pushed.VersionNumber}. Cần Writer nộp lại để bản chốt sản xuất trùng bản Editor đã đọc.",
                ResultErrorCodes.Conflict);

        // Đóng băng lại sau khi mở sửa: gắn cờ ảnh hưởng cho Panel có dòng vừa sửa (WF04)
        if (chapter.ScriptReopenStatus == ScriptReopenStatus.Approved && chapter.ScriptReopenRequestedAt.HasValue)
            await FlagImpactedWorkAsync(chapter.Id, chapter.ScriptReopenRequestedAt.Value, ct);

        // Mã băm dùng chung với phiên bản kịch bản (Trang → Panel → dòng)
        chapter.StartProduction(cmd.UserId, snapshot.Hash);
        chapter.UpdatedBy = cmd.UserId.ToString();

        await ScriptNotifications.NotifyWritersAsync(_dbContext, chapter, cmd.UserId,
            $"Kịch bản Chương {chapter.ChapterNumber} đã được chốt",
            $"Producer đã bắt đầu sản xuất Chương {chapter.ChapterNumber}. Kịch bản chuyển sang chỉ đọc.", ct);

        await _dbContext.SaveChangesAsync(ct);
        return Result<ChapterDetailDto>.Success(ChapterDetailDtoFactory.Create(chapter));
    }

    private async Task FlagImpactedWorkAsync(Guid chapterId, DateTime since, CancellationToken ct)
    {
        var modified = await _dbContext.ScriptLines
            .Where(l => l.PanelId != null && l.Panel!.Page.ChapterId == chapterId && l.UpdatedAt >= since)
            .Select(l => new { l.Id, PanelId = l.PanelId!.Value })
            .ToListAsync(ct);
        if (modified.Count == 0) return;

        var panelMap = modified.GroupBy(p => p.PanelId).ToDictionary(g => g.Key, g => g.First().Id);
        var panelIds = panelMap.Keys.ToList();

        foreach (var a in await _dbContext.Assignments
                     .Where(a => a.ChapterId == chapterId && panelIds.Contains(a.TargetEntityId)).ToListAsync(ct))
            a.FlagScriptImpact(panelMap[a.TargetEntityId]);

        foreach (var sp in await _dbContext.StageProgresses
                     .Where(sp => panelIds.Contains(sp.PanelId)).ToListAsync(ct))
            sp.FlagScriptImpact(panelMap[sp.PanelId]);
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

    public ScriptLifecycleCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<ChapterDetailDto>> Handle(SubmitScriptCommand cmd, CancellationToken ct)
    {
        var loaded = await LoadForWriterAsync(cmd.ChapterId, cmd.UserId, ct);
        if (!loaded.IsSuccess) return Result<ChapterDetailDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);
        var (chapter, workspaceId) = loaded.Value;

        // Gửi duyệt = push: commit bản hiện tại (nếu khác bản gần nhất) rồi đánh dấu đã gửi
        var version = await ScriptSnapshotBuilder.CommitAsync(_db, chapter.Id, cmd.UserId, cmd.Message, ct);
        if (version.LineCount == 0)
            return Result<ChapterDetailDto>.Failure("Kịch bản còn trống, chưa gửi duyệt được.", ResultErrorCodes.Validation);

        try
        {
            chapter.SubmitScript(cmd.UserId, version.Id);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ChapterDetailDto>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }
        version.MarkSubmitted(cmd.UserId);
        chapter.UpdatedBy = cmd.UserId.ToString();

        // Báo Editor (góp ý) và Producer (Start Production)
        var recipients = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.UserId != cmd.UserId &&
                        (m.Role == WorkspaceRole.Editor || m.Role == WorkspaceRole.Producer))
            .Select(m => m.UserId).Distinct().ToListAsync(ct);
        foreach (var userId in recipients)
        {
            _db.Notifications.Add(Notification.Create(
                userId,
                NotificationType.AssignmentSubmitted,
                $"Kịch bản Chương {chapter.ChapterNumber} chờ duyệt",
                $"Writer đã gửi duyệt kịch bản Chương {chapter.ChapterNumber}. Editor góp ý theo dòng; Producer bấm Bắt đầu sản xuất khi sẵn sàng.",
                chapterId: chapter.Id));
        }

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

        chapter.UnfreezeScript(cmd.UserId);
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

/// <summary>
/// V2.5 & WF04: Writer gửi yêu cầu mở lại kịch bản có lý do.
/// </summary>
public sealed record RequestScriptReopenCommand(Guid ChapterId, Guid UserId, string Reason) : IRequest<Result<ChapterDetailDto>>;

public sealed class RequestScriptReopenCommandHandler : IRequestHandler<RequestScriptReopenCommand, Result<ChapterDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public RequestScriptReopenCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ChapterDetailDto>> Handle(RequestScriptReopenCommand cmd, CancellationToken ct)
    {
        var chapter = await _dbContext.Chapters
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);

        if (chapter is null)
            return Result<ChapterDetailDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        try
        {
            chapter.RequestScriptReopen(cmd.UserId, cmd.Reason);
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            return Result<ChapterDetailDto>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }

        return Result<ChapterDetailDto>.Success(ChapterDetailDtoFactory.Create(chapter));
    }
}

/// <summary>
/// V2.5 & WF04: Producer duyệt mở lại kịch bản.
/// </summary>
public sealed record ApproveScriptReopenCommand(Guid ChapterId, Guid ProducerUserId) : IRequest<Result<ChapterDetailDto>>;

public sealed class ApproveScriptReopenCommandHandler : IRequestHandler<ApproveScriptReopenCommand, Result<ChapterDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public ApproveScriptReopenCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ChapterDetailDto>> Handle(ApproveScriptReopenCommand cmd, CancellationToken ct)
    {
        var chapter = await _dbContext.Chapters
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);

        if (chapter is null)
            return Result<ChapterDetailDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        try
        {
            chapter.ApproveScriptReopen(cmd.ProducerUserId);
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            return Result<ChapterDetailDto>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }

        return Result<ChapterDetailDto>.Success(ChapterDetailDtoFactory.Create(chapter));
    }
}

/// <summary>
/// V2.5 & WF04: Producer từ chối mở lại kịch bản.
/// </summary>
public sealed record RejectScriptReopenCommand(Guid ChapterId, Guid ProducerUserId) : IRequest<Result<ChapterDetailDto>>;

public sealed class RejectScriptReopenCommandHandler : IRequestHandler<RejectScriptReopenCommand, Result<ChapterDetailDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public RejectScriptReopenCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ChapterDetailDto>> Handle(RejectScriptReopenCommand cmd, CancellationToken ct)
    {
        var chapter = await _dbContext.Chapters
            .Include(c => c.Scenes)
            .Include(c => c.Pages)
            .FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);

        if (chapter is null)
            return Result<ChapterDetailDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        try
        {
            chapter.RejectScriptReopen(cmd.ProducerUserId);
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            return Result<ChapterDetailDto>.Failure(ex.Message, ResultErrorCodes.Conflict);
        }

        return Result<ChapterDetailDto>.Success(ChapterDetailDtoFactory.Create(chapter));
    }
}

internal static class ChapterDetailDtoFactory
{
    public static ChapterDetailDto Create(Chapter chapter)
    {
        var scenes = chapter.Scenes.OrderBy(s => s.SceneNumber).Select(s => new ChapterSceneSummaryDto(
            s.Id, s.SceneNumber, s.Heading, s.Summary)).ToList();
        var pages = chapter.Pages.OrderBy(p => p.PageNumber).Select(p => new ChapterPageSummaryDto(
            p.Id, p.PageNumber, p.LayoutFormat.ToString(), p.WidthPx, p.HeightPx, p.Dpi)).ToList();

        return new ChapterDetailDto(
            Id: chapter.Id,
            SeriesId: chapter.SeriesId,
            ChapterNumber: chapter.ChapterNumber,
            Title: chapter.Title,
            TargetReleaseDate: chapter.TargetReleaseDate,
            VersionVector: chapter.VersionVector,
            CreatedAt: chapter.CreatedAt,
            UpdatedAt: chapter.UpdatedAt,
            CreatedBy: chapter.CreatedBy,
            Scenes: scenes,
            Pages: pages,
            Status: chapter.Status,
            PublishedAt: chapter.PublishedAt,
            Outline: chapter.Outline,
            IsScriptFrozen: chapter.IsScriptFrozen,
            ScriptFrozenAt: chapter.ScriptFrozenAt,
            ScriptFrozenById: chapter.ScriptFrozenById,
            ScriptReopenStatus: chapter.ScriptReopenStatus,
            ScriptReopenReason: chapter.ScriptReopenReason,
            ScriptReopenRequestedById: chapter.ScriptReopenRequestedById,
            ScriptReopenRequestedAt: chapter.ScriptReopenRequestedAt,
            ScriptContentHash: chapter.ScriptContentHash,
            IsLocked: chapter.IsLocked,
            PriceVnd: chapter.PriceVnd,
            ScriptStatus: chapter.ScriptStatus,
            ScriptSubmittedAt: chapter.ScriptSubmittedAt,
            LastSubmittedVersionId: chapter.LastSubmittedVersionId,
            PreviousSubmittedVersionId: chapter.PreviousSubmittedVersionId
        );
    }
}
