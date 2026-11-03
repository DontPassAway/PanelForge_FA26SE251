using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Consistency.Models;

namespace PanelForge.Application.Features.Consistency.Commands.CheckPageVisualContinuity;

public sealed record PanelVisualContinuityDto(
    Guid PanelId, int PanelNumber, string Status, ImageConsistencyResultDto? Result, string? Error);

/// <summary>Status từng panel: "Passed" | "Warning" (đã tạo ConsistencyFinding mức Warning) | "Skipped" (không gọi AI) | "Failed" | "NotChecked".</summary>
public sealed record PageVisualContinuityDto(
    Guid PageId, int Passed, int Warnings, int Skipped, int Failed, IReadOnlyList<PanelVisualContinuityDto> Panels);

/// <summary>
/// Editor Visual Continuity (kế hoạch AI đa vai trò, Tuần 2): so ảnh từng panel của MỘT trang với character sheet đã khoá (BR-15).
/// Kết quả chỉ là cảnh báo (finding Warning, không chặn duyệt chương); không trả toạ độ vùng lỗi. Quyền Producer/Editor ở controller.
/// Theo trang để request có giới hạn (vài panel × vài giây); lỗi provider đầu tiên thì dừng, không gọi tiếp các panel còn lại.
/// </summary>
public sealed record CheckPageVisualContinuityCommand(Guid PageId, Guid UserId) : IRequest<Result<PageVisualContinuityDto>>;

public sealed class CheckPageVisualContinuityCommandHandler
    : IRequestHandler<CheckPageVisualContinuityCommand, Result<PageVisualContinuityDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IImageConsistencyService _vision;

    public CheckPageVisualContinuityCommandHandler(IPanelForgeDbContext db, IImageConsistencyService vision)
    {
        _db = db;
        _vision = vision;
    }

    public async Task<Result<PageVisualContinuityDto>> Handle(CheckPageVisualContinuityCommand cmd, CancellationToken ct)
    {
        if (!await _db.Pages.AsNoTracking().AnyAsync(p => p.Id == cmd.PageId, ct))
            return Result<PageVisualContinuityDto>.Failure($"Trang {cmd.PageId} không tồn tại.", ResultErrorCodes.NotFound);

        var panels = await _db.Panels.AsNoTracking().Where(p => p.PageId == cmd.PageId)
            .OrderBy(p => p.ReadingOrder).ThenBy(p => p.PanelNumber)
            .Select(p => new { p.Id, p.PanelNumber }).ToListAsync(ct);

        var results = new List<PanelVisualContinuityDto>();
        string? stopReason = null;
        foreach (var panel in panels)
        {
            if (stopReason is not null)
            {
                results.Add(new(panel.Id, panel.PanelNumber, "NotChecked", null, stopReason));
                continue;
            }
            try
            {
                var r = await _vision.CheckPanelConsistencyAsync(panel.Id, cancellationToken: ct);
                results.Add(new(panel.Id, panel.PanelNumber, r.Skipped ? "Skipped" : r.Passed ? "Passed" : "Warning", r, null));
            }
            catch (InvalidOperationException ex)
            {
                results.Add(new(panel.Id, panel.PanelNumber, "Failed", null, ex.Message));
                stopReason = "Dừng sau lỗi AI ở panel trước: " + ex.Message;
            }
        }

        if (panels.Count > 0 && results.All(r => r.Status is "Failed" or "NotChecked"))
            return Result<PageVisualContinuityDto>.Failure(results[0].Error!, ResultErrorCodes.AiUnavailable);

        return Result<PageVisualContinuityDto>.Success(new PageVisualContinuityDto(cmd.PageId,
            results.Count(r => r.Status == "Passed"), results.Count(r => r.Status == "Warning"),
            results.Count(r => r.Status == "Skipped"), results.Count(r => r.Status is "Failed" or "NotChecked"), results));
    }
}
