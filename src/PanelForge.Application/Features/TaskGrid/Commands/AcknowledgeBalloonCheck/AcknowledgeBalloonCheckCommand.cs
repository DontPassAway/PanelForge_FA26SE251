using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Commands.AcknowledgeBalloonCheck;

/// <summary>
/// ART-06 / BR-27.2: người làm tranh hoặc làm chữ của trang (hoặc Producer) xác nhận đã kiểm tra lại vị trí bóng thoại sau khi
/// tranh đổi, gỡ cờ Check Balloon Fit. <see cref="PanelId"/> null = mọi khung đang bật cờ của trang. Trả số khung đã gỡ cờ.
/// </summary>
public sealed record AcknowledgeBalloonCheckCommand(Guid ChapterId, Guid PageId, Guid UserId, Guid? PanelId = null)
    : IRequest<Result<BalloonCheckAckDto>>;

public sealed class AcknowledgeBalloonCheckCommandHandler : IRequestHandler<AcknowledgeBalloonCheckCommand, Result<BalloonCheckAckDto>>
{
    private readonly IPanelForgeDbContext _db;

    public AcknowledgeBalloonCheckCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<BalloonCheckAckDto>> Handle(AcknowledgeBalloonCheckCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Result<BalloonCheckAckDto>.Failure($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (!await _db.Pages.AsNoTracking().AnyAsync(p => p.Id == cmd.PageId && p.ChapterId == chapter.Id, ct))
            return Result<BalloonCheckAckDto>.Failure("Page không thuộc Chapter.", ResultErrorCodes.NotFound);

        // Người giữ ô vẽ hoặc ô chữ của trang này, hoặc Producer của Workspace
        var holdsPage = await (from t in _db.StageTasks.AsNoTracking()
                               join l in _db.StageTaskLots.AsNoTracking() on t.LotId equals (Guid?)l.Id
                               join s in _db.PipelineStages.AsNoTracking() on t.PipelineStageId equals s.Id
                               where t.ChapterId == chapter.Id && t.PageId == cmd.PageId && t.Status != StageTaskStatus.Cancelled &&
                                     l.AssigneeUserId == cmd.UserId &&
                                     (s.AllowedRole == WorkspaceRole.Artist || s.AllowedRole == WorkspaceRole.Letterer)
                               select t.Id).AnyAsync(ct);
        if (!holdsPage)
        {
            var workspaceId = await _db.Series.AsNoTracking()
                .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
            var isProducer = await _db.WorkspaceMembers.AsNoTracking().AnyAsync(m =>
                m.WorkspaceId == workspaceId && m.UserId == cmd.UserId && m.Status == WorkspaceMemberStatus.Active &&
                m.Role == WorkspaceRole.Producer, ct);
            if (!isProducer)
                return Result<BalloonCheckAckDto>.Failure("Chỉ người làm tranh, làm chữ của trang hoặc Producer mới xác nhận được.", ResultErrorCodes.Forbidden);
        }

        var panels = await _db.Panels
            .Where(p => p.PageId == cmd.PageId && p.BalloonCheckRequestedAt != null && (cmd.PanelId == null || p.Id == cmd.PanelId))
            .ToListAsync(ct);
        foreach (var panel in panels)
        {
            panel.AcknowledgeBalloonCheck();
            panel.UpdatedBy = cmd.UserId.ToString();
        }
        if (panels.Count > 0) await _db.SaveChangesAsync(ct);
        return Result<BalloonCheckAckDto>.Success(new(panels.Count));
    }
}
