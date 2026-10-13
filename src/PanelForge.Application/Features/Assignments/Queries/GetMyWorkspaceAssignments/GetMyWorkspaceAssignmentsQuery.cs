using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Queries.GetMyWorkspaceAssignments;

/// <summary>
/// Truy vấn toàn bộ công việc được giao của một cá nhân xuyên suốt toàn bộ Studio/Workspace (Cross-Chapter Worklist).
/// Không bị giới hạn trong 1 Chapter duy nhất.
/// </summary>
public sealed record GetMyWorkspaceAssignmentsQuery(
    Guid WorkspaceId,
    Guid UserId,
    AssignmentStatus? Status = null,
    Guid? SeriesId = null
) : IRequest<Result<IReadOnlyList<WorkspaceAssignmentDto>>>;

public sealed class GetMyWorkspaceAssignmentsQueryHandler
    : IRequestHandler<GetMyWorkspaceAssignmentsQuery, Result<IReadOnlyList<WorkspaceAssignmentDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetMyWorkspaceAssignmentsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<WorkspaceAssignmentDto>>> Handle(
        GetMyWorkspaceAssignmentsQuery q, CancellationToken ct)
    {
        var workspaceExists = await _db.StudioWorkspaces.AsNoTracking().AnyAsync(w => w.Id == q.WorkspaceId, ct);
        if (!workspaceExists)
            return Result<IReadOnlyList<WorkspaceAssignmentDto>>.Failure($"Workspace {q.WorkspaceId} không tồn tại.", ResultErrorCodes.NotFound);

        var seriesQuery = _db.Series.AsNoTracking().Where(s => s.WorkspaceId == q.WorkspaceId);
        if (q.SeriesId.HasValue)
        {
            seriesQuery = seriesQuery.Where(s => s.Id == q.SeriesId.Value);
        }

        var baseQuery = from a in _db.Assignments.AsNoTracking()
                        join c in _db.Chapters.AsNoTracking() on a.ChapterId equals c.Id
                        join s in seriesQuery on a.SeriesId equals s.Id
                        join stage in _db.PipelineStages.AsNoTracking() on a.PipelineStageId equals stage.Id
                        where a.AssigneeUserId == q.UserId
                        select new
                        {
                            Assignment = a,
                            SeriesTitle = s.Title,
                            c.ChapterNumber,
                            ChapterTitle = c.Title,
                            StageName = stage.Name,
                            stage.StageOrder,
                            StageColorCode = stage.ColorCode
                        };

        if (q.Status.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Assignment.Status == q.Status.Value);
        }

        var rawList = await baseQuery
            .OrderBy(x => x.Assignment.Status == AssignmentStatus.Approved)
            .ThenBy(x => x.Assignment.DueDate == null)
            .ThenBy(x => x.Assignment.DueDate)
            .ThenByDescending(x => x.Assignment.Priority)
            .ThenBy(x => x.Assignment.CreatedAt)
            .ToListAsync(ct);

        var dtos = rawList.Select(x => new WorkspaceAssignmentDto(
            x.Assignment.Id,
            x.Assignment.SeriesId,
            x.SeriesTitle,
            x.Assignment.ChapterId,
            x.ChapterNumber,
            x.ChapterTitle,
            x.Assignment.PipelineStageId,
            x.StageName,
            x.StageOrder,
            x.StageColorCode,
            x.Assignment.EntityType,
            x.Assignment.TargetEntityId,
            x.Assignment.AssigneeUserId,
            x.Assignment.AssignedRole,
            x.Assignment.Brief,
            x.Assignment.ReferenceNotes,
            x.Assignment.DueDate,
            x.Assignment.Priority,
            x.Assignment.Status,
            x.Assignment.DependsOnAssignmentId,
            x.Assignment.StartedAt,
            x.Assignment.CompletedAt,
            x.Assignment.LastFeedbackComment,
            x.Assignment.CreatedAt,
            x.Assignment.UpdatedAt,
            x.Assignment.GetDependsOnAssignmentIds()
        )).ToList();

        return Result<IReadOnlyList<WorkspaceAssignmentDto>>.Success(dtos);
    }
}
