using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;

namespace PanelForge.Application.Features.Workspaces.Queries.GetMyWorkspaces;

/// <summary>
/// GET /api/workspaces — các Studio mà user sở hữu hoặc là thành viên (BR-23), kèm số task chưa xong,
/// số series, 3 series cập nhật gần nhất và thời điểm hoạt động gần nhất (series, chương hoặc việc).
/// </summary>
public sealed record GetMyWorkspacesQuery(Guid UserId) : IRequest<Result<IReadOnlyList<WorkspaceDto>>>;

public sealed class GetMyWorkspacesQueryHandler
    : IRequestHandler<GetMyWorkspacesQuery, Result<IReadOnlyList<WorkspaceDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetMyWorkspacesQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<WorkspaceDto>>> Handle(GetMyWorkspacesQuery query, CancellationToken cancellationToken)
    {
        var userId = query.UserId;

        var workspaces = await _dbContext.StudioWorkspaces
            .Include(w => w.Owner)
            .Include(w => w.Members.Where(m => !m.IsDeleted && m.Status == WorkspaceMemberStatus.Active))
                .ThenInclude(m => m.User)
            .Where(w => !w.IsDeleted && (w.OwnerId == userId || w.Members.Any(m => m.UserId == userId && !m.IsDeleted && m.Status == WorkspaceMemberStatus.Active)))
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        var workspaceIds = workspaces.Select(w => w.Id).ToList();
        var pendingByWorkspace = await _dbContext.Assignments
            .Where(a => !a.IsDeleted && a.AssigneeUserId == userId && a.Status != AssignmentStatus.Approved
                        && workspaceIds.Contains(a.Series.WorkspaceId))
            .GroupBy(a => a.Series.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, cancellationToken);

        var pendingReviewsByWorkspace = await _dbContext.Assignments
            .Where(a => !a.IsDeleted && (a.Status == AssignmentStatus.Submitted || a.Status == AssignmentStatus.UnderReview)
                        && a.AssigneeUserId != userId
                        && workspaceIds.Contains(a.Series.WorkspaceId))
            .GroupBy(a => a.Series.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, cancellationToken);

        // Hoạt động của Studio: series (kèm ảnh bìa), chương và việc được tạo/cập nhật gần nhất
        var seriesRows = await _dbContext.Series
            .Where(s => !s.IsDeleted && workspaceIds.Contains(s.WorkspaceId))
            .Select(s => new { s.Id, s.WorkspaceId, s.Title, s.CoverImageSha256, At = s.UpdatedAt ?? s.CreatedAt })
            .ToListAsync(cancellationToken);

        var chapterActivity = await _dbContext.Chapters
            .Where(c => !c.IsDeleted && workspaceIds.Contains(c.Series.WorkspaceId))
            .GroupBy(c => c.Series.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, At = g.Max(c => c.UpdatedAt ?? c.CreatedAt) })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.At, cancellationToken);

        var assignmentActivity = await _dbContext.Assignments
            .Where(a => !a.IsDeleted && workspaceIds.Contains(a.Series.WorkspaceId))
            .GroupBy(a => a.Series.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, At = g.Max(a => a.UpdatedAt ?? a.CreatedAt) })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.At, cancellationToken);

        WorkspaceMapper.WorkspaceActivity ActivityOf(Guid workspaceId)
        {
            var rows = seriesRows.Where(s => s.WorkspaceId == workspaceId).OrderByDescending(s => s.At).ToList();
            DateTime? chapterAt = chapterActivity.TryGetValue(workspaceId, out var ch) ? ch : null;
            DateTime? assignmentAt = assignmentActivity.TryGetValue(workspaceId, out var asg) ? asg : null;
            var last = new[] { rows.FirstOrDefault()?.At, chapterAt, assignmentAt }.Max();
            var previews = rows
                .Take(3)
                .Select(s => new WorkspaceSeriesPreviewDto(s.Id, s.Title, SetSeriesCoverCommandHandler.CoverUrl(s.Id, s.CoverImageSha256)))
                .ToList();
            return new WorkspaceMapper.WorkspaceActivity(rows.Count, previews, last);
        }

        IReadOnlyList<WorkspaceDto> result = workspaces
            .Select(w =>
            {
                var previews = w.Members
                    .Where(m => !m.IsDeleted && m.User != null)
                    .Take(4)
                    .Select(m => new WorkspaceMemberPreviewDto(m.UserId, m.User.FullName, m.User.AvatarUrl))
                    .ToList();

                return WorkspaceMapper.ToDto(
                    w,
                    userId,
                    memberCount: w.Members.Count(m => !m.IsDeleted),
                    pendingTasks: pendingByWorkspace.GetValueOrDefault(w.Id),
                    memberPreviews: previews,
                    pendingEditorReviews: pendingReviewsByWorkspace.GetValueOrDefault(w.Id),
                    activity: ActivityOf(w.Id));
            })
            .ToList();

        return Result<IReadOnlyList<WorkspaceDto>>.Success(result);
    }
}
