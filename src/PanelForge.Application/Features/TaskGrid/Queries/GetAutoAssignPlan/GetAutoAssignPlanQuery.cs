using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.TaskGrid.Queries.GetAutoAssignPlan;

/// <summary>
/// Lập phương án giao các ô chưa giao của chương; không ghi gì. Người dùng xác nhận bằng POST /lots.
/// Cân theo số ô chưa xong của từng người trong Workspace, giữ trang liền nhau và ưu tiên người đã làm trang đầu của chương.
/// </summary>
public sealed record GetAutoAssignPlanQuery(Guid ChapterId) : IRequest<Result<AutoAssignPlanDto>>;

public sealed class GetAutoAssignPlanQueryHandler : IRequestHandler<GetAutoAssignPlanQuery, Result<AutoAssignPlanDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetAutoAssignPlanQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AutoAssignPlanDto>> Handle(GetAutoAssignPlanQuery q, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == q.ChapterId, ct);
        if (chapter is null)
            return Result<AutoAssignPlanDto>.Failure($"Chapter {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Result<AutoAssignPlanDto>.Failure(locked, ResultErrorCodes.Conflict);

        var workspaceId = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        var seriesIds = await _db.Series.AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId).Select(s => s.Id).ToListAsync(ct);
        var members = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Status == WorkspaceMemberStatus.Active)
            .Select(m => new { m.UserId, m.Role }).ToListAsync(ct);
        var memberIds = members.Select(m => m.UserId).ToList();
        var names = await _db.Users.AsNoTracking().Where(u => memberIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var openCounts = (await (
                from t in _db.StageTasks.AsNoTracking()
                join l in _db.StageTaskLots.AsNoTracking() on t.LotId equals (Guid?)l.Id
                where memberIds.Contains(l.AssigneeUserId) && seriesIds.Contains(t.SeriesId)
                      && t.Status != StageTaskStatus.Approved && t.Status != StageTaskStatus.Cancelled
                select l.AssigneeUserId).ToListAsync(ct))
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        var load = memberIds.ToDictionary(id => id, id => openCounts.GetValueOrDefault(id));

        var pageNumbers = await _db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id)
            .ToDictionaryAsync(p => p.Id, p => p.PageNumber, ct);
        var tasks = await _db.StageTasks.AsNoTracking().Where(t => t.ChapterId == chapter.Id).ToListAsync(ct);
        var assigneeByLot = await _db.StageTaskLots.AsNoTracking().Where(l => l.ChapterId == chapter.Id)
            .ToDictionaryAsync(l => l.Id, l => l.AssigneeUserId, ct);

        // Trang đầu mỗi người đang giữ trong chương: giúp công đoạn sau nối đúng phần trang của họ
        var firstPage = new Dictionary<Guid, int>();
        void NotePage(Guid userId, int page) =>
            firstPage[userId] = firstPage.TryGetValue(userId, out var current) ? Math.Min(current, page) : page;
        foreach (var t in tasks)
            if (t.LotId is { } lotId && t.PageId is { } pageId && t.Status != StageTaskStatus.Cancelled
                && assigneeByLot.TryGetValue(lotId, out var owner) && pageNumbers.TryGetValue(pageId, out var number))
                NotePage(owner, number);

        var lots = new List<AutoAssignLotDto>();
        var skipped = new List<AutoAssignSkippedDto>();
        var unassigned = tasks.Where(t => t.Status == StageTaskStatus.Unassigned).ToList();

        foreach (var stage in (await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct)).Where(s => s.IsEnabled))
        {
            var cells = unassigned.Where(t => t.PipelineStageId == stage.Id)
                .OrderBy(t => t.PageId is { } id ? pageNumbers.GetValueOrDefault(id) : 0).ToList();
            if (cells.Count == 0) continue;

            if (stage.AllowedRole is not { } role)
            {
                skipped.Add(new(stage.Id, stage.Name, cells.Count, "Công đoạn chưa cấu hình vai trò phụ trách."));
                continue;
            }
            var candidates = members.Where(m => m.Role == role).Select(m => m.UserId).OrderBy(id => id).ToList();
            if (candidates.Count == 0)
            {
                skipped.Add(new(stage.Id, stage.Name, cells.Count, $"Không có thành viên Active vai trò {role}."));
                continue;
            }

            // Từng ô đưa cho người đang nhẹ việc nhất để tổng việc cân nhau
            var shares = candidates.ToDictionary(id => id, _ => 0);
            foreach (var _ in cells)
                shares[candidates.MinBy(id => load[id] + shares[id])]++;

            var taken = 0;
            foreach (var userId in candidates.Where(id => shares[id] > 0)
                         .OrderBy(id => firstPage.GetValueOrDefault(id, int.MaxValue)).ThenBy(id => load[id]))
            {
                var part = cells.Skip(taken).Take(shares[userId]).ToList();
                taken += part.Count;
                var pages = part.Where(t => t.PageId is not null && pageNumbers.ContainsKey(t.PageId.Value))
                    .Select(t => pageNumbers[t.PageId!.Value]).ToList();
                lots.Add(new(stage.Id, stage.Name, userId, names.GetValueOrDefault(userId, ""), role,
                    part.Select(t => t.Id).ToList(), pages, openCounts.GetValueOrDefault(userId)));
                foreach (var page in pages) NotePage(userId, page);
            }
            foreach (var (userId, share) in shares) load[userId] += share;
        }

        return Result<AutoAssignPlanDto>.Success(new(chapter.Id, unassigned.Count, lots, skipped));
    }
}
