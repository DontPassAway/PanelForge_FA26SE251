using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Consistency.Common;
using PanelForge.Application.Features.Consistency.Models;
using PanelForge.Application.Features.Consistency.Queries;

namespace PanelForge.Application.Features.Consistency.Commands;

/// <summary>
/// CF3 (V1.2): kiểm nhất quán hình ảnh cả Chapter = chạy <see cref="IImageConsistencyService"/> (đường kiểm ảnh duy nhất, có gọi AI thật)
/// cho từng panel, theo thứ tự trang. Finding mức Warning (không chặn duyệt chương). Lỗi AI đầu tiên thì dừng, không gọi tiếp.
/// Finding VISUAL_CHARACTER_DEVIATION cũ (điểm giả tính từ hash, trước khi có AI) được đánh dấu không còn phát hiện.
/// </summary>
public sealed record RunVisualConsistencyCheckCommand(
    Guid ChapterId,
    Guid UserId,
    decimal? Threshold = null
) : IRequest<Result<VisualConsistencyReportDto>>;

public sealed class RunVisualConsistencyCheckCommandHandler
    : IRequestHandler<RunVisualConsistencyCheckCommand, Result<VisualConsistencyReportDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IImageConsistencyService _vision;

    public RunVisualConsistencyCheckCommandHandler(IPanelForgeDbContext db, IImageConsistencyService vision)
    {
        _db = db;
        _vision = vision;
    }

    public async Task<Result<VisualConsistencyReportDto>> Handle(RunVisualConsistencyCheckCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<VisualConsistencyReportDto>.Failure($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        var threshold = cmd.Threshold ?? ConsistencyChecker.DefaultVisualSimilarityThreshold;

        var panelIds = await (from pn in _db.Panels.AsNoTracking()
                              join pg in _db.Pages.AsNoTracking() on pn.PageId equals pg.Id
                              where pg.ChapterId == chapter.Id
                              orderby pg.PageNumber, pn.ReadingOrder, pn.PanelNumber
                              select pn.Id).ToListAsync(ct);

        int evaluated = 0, passed = 0;
        foreach (var panelId in panelIds)
        {
            try
            {
                var r = await _vision.CheckPanelConsistencyAsync(panelId, threshold: threshold, cancellationToken: ct);
                if (r.Skipped) continue;
                evaluated++;
                if (r.Passed) passed++;
            }
            catch (InvalidOperationException ex)
            {
                await RetireLegacyAsync(chapter.Id, ct);
                return Result<VisualConsistencyReportDto>.Failure(
                    $"Dừng sau {evaluated} panel đã kiểm do lỗi AI: {ex.Message}", ResultErrorCodes.AiUnavailable);
            }
        }

        await RetireLegacyAsync(chapter.Id, ct);
        return Result<VisualConsistencyReportDto>.Success(
            await GetVisualConsistencyReportQueryHandler.BuildAsync(_db, chapter, threshold, evaluated, passed, ct));
    }

    private async Task RetireLegacyAsync(Guid chapterId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var legacy = await _db.ConsistencyFindings
            .Where(f => f.ChapterId == chapterId && f.RuleCode == ConsistencyChecker.VisualCharacterDeviation)
            .ToListAsync(ct);
        foreach (var f in legacy) f.NoLongerDetected(now);
        if (legacy.Count > 0) await _db.SaveChangesAsync(ct);
    }
}
