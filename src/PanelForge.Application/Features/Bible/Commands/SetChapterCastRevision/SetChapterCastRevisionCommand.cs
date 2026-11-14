using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.SetChapterCastRevision;

public sealed record SetChapterCastRevisionResultDto(
    Guid ChapterId,
    Guid BibleEntryId,
    Guid? BoundRevisionId,
    bool ChosenByWriter,
    IReadOnlyList<decimal> AffectedChapterNumbers,
    IReadOnlyList<string> Warnings
);

/// <summary>
/// BIB-13 (Q18, Q19, Q23, Q24): Writer đổi bản cho chương hoặc trả về bản kế thừa.
/// Chỉ áp dụng khi kịch bản chưa Frozen. Tính lại kế thừa cho các chương phía sau
/// và cảnh báo các chương sau đã Frozen với bản khác.
/// </summary>
public sealed record SetChapterCastRevisionCommand(
    Guid ChapterId,
    Guid BibleEntryId,
    Guid? RevisionId,
    Guid UserId
) : IRequest<Result<SetChapterCastRevisionResultDto>>;

public sealed class SetChapterCastRevisionCommandHandler
    : IRequestHandler<SetChapterCastRevisionCommand, Result<SetChapterCastRevisionResultDto>>
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IChapterCastInheritanceResolver _inheritanceResolver;

    public SetChapterCastRevisionCommandHandler(
        IPanelForgeDbContext dbContext,
        IChapterCastInheritanceResolver inheritanceResolver)
    {
        _dbContext = dbContext;
        _inheritanceResolver = inheritanceResolver;
    }

    public async Task<Result<SetChapterCastRevisionResultDto>> Handle(
        SetChapterCastRevisionCommand cmd,
        CancellationToken ct)
    {
        var chapter = await _dbContext.Chapters
            .FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);

        if (chapter == null)
        {
            return Result<SetChapterCastRevisionResultDto>.Failure(
                $"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        }

        if (chapter.ScriptStatus == ScriptStatus.Frozen || chapter.IsScriptFrozen)
        {
            return Result<SetChapterCastRevisionResultDto>.Failure(
                "Kịch bản chương đã đóng băng (Frozen), không thể thay đổi bản của thực thể.", ResultErrorCodes.Conflict);
        }

        var entry = await _dbContext.BibleEntries
            .Include(e => e.Revisions).ThenInclude(r => r.Events)
            .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId, ct);

        if (entry == null)
        {
            return Result<SetChapterCastRevisionResultDto>.Failure(
                $"Mục BibleEntry {cmd.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);
        }

        var binding = await _dbContext.ChapterBibleBindings
            .FirstOrDefaultAsync(b => b.ChapterId == cmd.ChapterId && b.BibleEntryId == cmd.BibleEntryId, ct);

        if (binding == null)
        {
            return Result<SetChapterCastRevisionResultDto>.Failure(
                $"Thực thể '{entry.Name}' không thuộc danh sách thực thể của chương này.", ResultErrorCodes.NotFound);
        }

        if (cmd.RevisionId.HasValue)
        {
            var targetRevision = entry.Revisions.FirstOrDefault(r => r.Id == cmd.RevisionId.Value);
            if (targetRevision == null)
            {
                return Result<SetChapterCastRevisionResultDto>.Failure(
                    $"Revision {cmd.RevisionId.Value} không thuộc mục Bible '{entry.Name}'.", ResultErrorCodes.Conflict);
            }

            var isSuperseded = targetRevision.Events.Any(e => e.Type == BibleRevisionEventType.Superseded)
                || entry.Revisions.Any(other => other.SupersedesRevisionId == targetRevision.Id && other.IsLocked);

            if (isSuperseded)
            {
                return Result<SetChapterCastRevisionResultDto>.Failure(
                    "Không thể chọn bản đã bị thay thế (Superseded).", ResultErrorCodes.Conflict);
            }

            // Gắn bản do Writer tự chọn (kể cả chưa khoá)
            binding.UpdateBinding(
                boundRevisionId: targetRevision.Id,
                source: binding.Source,
                chosenByWriter: true,
                boundById: cmd.UserId);
        }
        else
        {
            // Trả về bản kế thừa
            binding.UpdateBinding(
                boundRevisionId: null,
                source: binding.Source,
                chosenByWriter: false,
                boundById: cmd.UserId);
        }

        // Tính lại bản kế thừa cho chương này (nếu chosen_by_writer = false) và các chương sau chưa Frozen
        await _inheritanceResolver.ResolveInheritedAsync(chapter.SeriesId, (int)chapter.ChapterNumber, ct);

        // Thu thập các chương phía sau kế thừa theo và cảnh báo các chương phía sau đã chốt với bản khác
        var laterChapters = await _dbContext.Chapters
            .Where(c => c.SeriesId == chapter.SeriesId && c.ChapterNumber > chapter.ChapterNumber)
            .OrderBy(c => c.ChapterNumber)
            .ToListAsync(ct);

        var laterChapterIds = laterChapters.Select(c => c.Id).ToList();
        var laterBindings = await _dbContext.ChapterBibleBindings
            .Where(b => b.BibleEntryId == cmd.BibleEntryId && laterChapterIds.Contains(b.ChapterId))
            .ToListAsync(ct);

        var affectedChapterNumbers = new List<decimal>();
        var warnings = new List<string>();

        foreach (var laterCh in laterChapters)
        {
            var lb = laterBindings.FirstOrDefault(b => b.ChapterId == laterCh.Id);
            if (lb == null) continue;

            var isLaterFrozen = laterCh.ScriptStatus == ScriptStatus.Frozen || laterCh.IsScriptFrozen;
            if (!isLaterFrozen && !lb.ChosenByWriter)
            {
                affectedChapterNumbers.Add(laterCh.ChapterNumber);
            }
            else if (isLaterFrozen && lb.BoundRevisionId != binding.BoundRevisionId)
            {
                warnings.Add($"Chương {laterCh.ChapterNumber} đã đóng băng kịch bản với bản khác.");
            }
        }

        // Ghi Audit log (BR-18, Q23)
        var workspaceId = await _dbContext.Series
            .Where(s => s.Id == chapter.SeriesId)
            .Select(s => (Guid?)s.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        _dbContext.AuditLogs.Add(AuditLog.Create(
            action: "SET_CHAPTER_CAST_REVISION",
            entityName: nameof(ChapterBibleBinding),
            entityId: $"{chapter.Id}:{cmd.BibleEntryId}",
            workspaceId: workspaceId,
            userId: cmd.UserId,
            details: $"Đổi bản thực thể '{entry.Name}' cho Chương {chapter.ChapterNumber} thành {(cmd.RevisionId.HasValue ? cmd.RevisionId.Value.ToString() : "bản kế thừa")} (ChosenByWriter={binding.ChosenByWriter})."));

        await _dbContext.SaveChangesAsync(ct);

        return Result<SetChapterCastRevisionResultDto>.Success(new SetChapterCastRevisionResultDto(
            ChapterId: chapter.Id,
            BibleEntryId: entry.Id,
            BoundRevisionId: binding.BoundRevisionId,
            ChosenByWriter: binding.ChosenByWriter,
            AffectedChapterNumbers: affectedChapterNumbers,
            Warnings: warnings));
    }
}
