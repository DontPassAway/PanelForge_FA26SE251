using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Bible.Commands.SetBibleEntryCrowd;

/// <summary>
/// BIB-13 (7A, Q19): Bật/tắt cờ Quần chúng cho mục Bible.
/// Cập nhật is_crowd_at_bind cho mọi chương chưa Frozen có mục đó.
/// Chương đã Frozen giữ nguyên. Chỉ Writer được gọi.
/// </summary>
public sealed record SetBibleEntryCrowdCommand(
    Guid SeriesId,
    Guid BibleEntryId,
    bool IsCrowd,
    Guid UserId
) : IRequest<Result<BibleEntryDto>>;

public sealed class SetBibleEntryCrowdCommandHandler : IRequestHandler<SetBibleEntryCrowdCommand, Result<BibleEntryDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public SetBibleEntryCrowdCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<BibleEntryDto>> Handle(SetBibleEntryCrowdCommand cmd, CancellationToken ct)
    {
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == cmd.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        if (!bibleId.HasValue)
        {
            return Result<BibleEntryDto>.Failure(
                $"Series {cmd.SeriesId} không tồn tại hoặc chưa có Series Bible.", ResultErrorCodes.NotFound);
        }

        var entry = await _dbContext.BibleEntries
            .Include(e => e.Revisions)
            .FirstOrDefaultAsync(e => e.Id == cmd.BibleEntryId && e.SeriesBibleId == bibleId.Value, ct);

        if (entry == null)
        {
            return Result<BibleEntryDto>.Failure(
                $"Mục BibleEntry {cmd.BibleEntryId} không tồn tại.", ResultErrorCodes.NotFound);
        }

        entry.SetCrowd(cmd.IsCrowd);
        entry.UpdatedBy = cmd.UserId.ToString();

        // 7A: cập nhật is_crowd_at_bind cho mọi chương cùng Series chưa Frozen
        var nonFrozenChapterIds = await _dbContext.Chapters
            .Where(c => c.SeriesId == cmd.SeriesId && c.ScriptStatus != ScriptStatus.Frozen && !c.IsScriptFrozen)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (nonFrozenChapterIds.Count > 0)
        {
            var affectedBindings = await _dbContext.ChapterBibleBindings
                .Where(b => b.BibleEntryId == entry.Id && nonFrozenChapterIds.Contains(b.ChapterId))
                .ToListAsync(ct);

            foreach (var binding in affectedBindings)
            {
                binding.SetCrowdAtBind(cmd.IsCrowd);
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        return Result<BibleEntryDto>.Success(BibleEntryDto.From(entry));
    }
}
