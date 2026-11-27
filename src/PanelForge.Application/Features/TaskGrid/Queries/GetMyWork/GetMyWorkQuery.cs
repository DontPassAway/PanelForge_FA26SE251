using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Application.Features.TaskGrid.Queries.GetMyTaskLots;
using PanelForge.Application.Features.TaskGrid.Queries.GetTaskGridReviewQueue;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Queries.GetMyWork;

/// <summary>Việc của tôi ở một Studio: lô được giao (Task Grid) và hàng chờ duyệt của các Series tôi làm Editor.</summary>
public sealed record MyWorkStudioDto(
    Guid WorkspaceId, string WorkspaceName, IReadOnlyList<MyTaskLotDto> Lots, IReadOnlyList<TaskGridReviewItemDto> ReviewQueue);

/// <summary>
/// UIQ-11q: màn "Việc của tôi" xuyên Studio cho Writer/Artist/Letterer/Editor. Gộp lô chưa đóng (bỏ lô Completed) ở mọi
/// Studio người gọi là thành viên Active hoặc chủ sở hữu, cộng hàng chờ duyệt của các Series họ giữ vai trò Editor
/// (spec D10: vai trò theo Series). Studio không có việc nào thì không trả về.
/// </summary>
public sealed record GetMyWorkQuery(Guid UserId) : IRequest<Result<IReadOnlyList<MyWorkStudioDto>>>;

public sealed class GetMyWorkQueryHandler : IRequestHandler<GetMyWorkQuery, Result<IReadOnlyList<MyWorkStudioDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetMyWorkQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<MyWorkStudioDto>>> Handle(GetMyWorkQuery q, CancellationToken ct)
    {
        var workspaceIds = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.UserId == q.UserId && m.Status == WorkspaceMemberStatus.Active)
            .Select(m => m.WorkspaceId)
            .ToListAsync(ct);
        var studios = await _db.StudioWorkspaces.AsNoTracking()
            .Where(w => workspaceIds.Contains(w.Id) || w.OwnerId == q.UserId)
            .Select(w => new { w.Id, w.Name })
            .ToListAsync(ct);

        var editorSeries = await _db.SeriesMembers.AsNoTracking()
            .Where(sm => sm.UserId == q.UserId && !sm.IsDeleted && sm.Role == WorkspaceRole.Editor)
            .Select(sm => sm.SeriesId)
            .ToListAsync(ct);

        var lotsHandler = new GetMyTaskLotsQueryHandler(_db);
        var queueHandler = new GetTaskGridReviewQueueQueryHandler(_db);
        var result = new List<MyWorkStudioDto>();
        foreach (var studio in studios.OrderBy(s => s.Name))
        {
            var lots = await lotsHandler.Handle(new GetMyTaskLotsQuery(studio.Id, q.UserId, IncludeCompleted: false), ct);
            if (!lots.IsSuccess) return Result<IReadOnlyList<MyWorkStudioDto>>.Failure(lots.ErrorMessage!, lots.ErrorCode);

            var queue = new List<TaskGridReviewItemDto>();
            var studioEditorSeries = await _db.Series.AsNoTracking()
                .Where(s => s.WorkspaceId == studio.Id && editorSeries.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync(ct);
            foreach (var seriesId in studioEditorSeries)
            {
                var items = await queueHandler.Handle(new GetTaskGridReviewQueueQuery(studio.Id, seriesId), ct);
                if (items.IsSuccess) queue.AddRange(items.Value!);
            }

            if (lots.Value!.Count > 0 || queue.Count > 0)
                result.Add(new MyWorkStudioDto(studio.Id, studio.Name, lots.Value!, queue));
        }

        return Result<IReadOnlyList<MyWorkStudioDto>>.Success(result);
    }
}
