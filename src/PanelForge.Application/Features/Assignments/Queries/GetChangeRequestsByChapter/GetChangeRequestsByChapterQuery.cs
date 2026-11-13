using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Queries.GetChangeRequestsByChapter;

/// <summary>Danh sách yêu cầu sửa của Chapter (UC-17), lọc tuỳ chọn theo việc, trạng thái, người chịu trách nhiệm. Quá hạn nằm ở <c>dueDate</c>.</summary>
public sealed record GetChangeRequestsByChapterQuery(
    Guid ChapterId,
    Guid? AssignmentId = null,
    ChangeRequestStatus? Status = null,
    Guid? ResponsibleUserId = null
) : IRequest<Result<IReadOnlyList<ChangeRequestDto>>>;

public sealed class GetChangeRequestsByChapterQueryHandler
    : IRequestHandler<GetChangeRequestsByChapterQuery, Result<IReadOnlyList<ChangeRequestDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetChangeRequestsByChapterQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<ChangeRequestDto>>> Handle(GetChangeRequestsByChapterQuery q, CancellationToken ct)
    {
        if (!await _db.Chapters.AsNoTracking().AnyAsync(c => c.Id == q.ChapterId, ct))
            return Result<IReadOnlyList<ChangeRequestDto>>.Failure($"Chapter {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var query = _db.ChangeRequests.AsNoTracking().Where(c => c.ChapterId == q.ChapterId);
        if (q.AssignmentId is { } assignmentId) query = query.Where(c => c.AssignmentId == assignmentId);
        if (q.Status is { } status) query = query.Where(c => c.Status == status);
        if (q.ResponsibleUserId is { } responsible) query = query.Where(c => c.ResponsibleUserId == responsible);

        var rows = await query.OrderBy(c => c.CreatedAt).ToListAsync(ct);

        // Trang + công đoạn để màn làm việc đưa người sửa tới đúng chỗ ("Xem vị trí").
        // Trả việc chỉ nhận việc cấp Page / Panel (ReturnWorkFromReview), nên luôn suy ra được trang:
        // khung được khoanh → trang của khung; nếu không thì từ đối tượng của việc bị trả về.
        var assignmentIds = rows.Select(r => r.AssignmentId).Distinct().ToList();
        var assignments = await _db.Assignments.AsNoTracking()
            .Where(a => assignmentIds.Contains(a.Id))
            .Select(a => new { a.Id, a.EntityType, a.TargetEntityId, a.PipelineStageId })
            .ToDictionaryAsync(a => a.Id, ct);
        var panelIds = rows.Where(r => r.PanelId != null).Select(r => r.PanelId!.Value)
            .Concat(assignments.Values.Where(a => a.EntityType == WorkflowEntityType.Panel).Select(a => a.TargetEntityId))
            .Distinct().ToList();
        var panelPage = await _db.Panels.AsNoTracking()
            .Where(p => panelIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PageId, ct);

        Guid? PageOf(Domain.Entities.Workflow.ChangeRequest c)
        {
            if (c.PanelId is { } pid && panelPage.TryGetValue(pid, out var fromPanel)) return fromPanel;
            if (!assignments.TryGetValue(c.AssignmentId, out var a)) return null;
            return a.EntityType switch
            {
                WorkflowEntityType.Page => a.TargetEntityId,
                WorkflowEntityType.Panel when panelPage.TryGetValue(a.TargetEntityId, out var pg) => pg,
                _ => null,
            };
        }

        return Result<IReadOnlyList<ChangeRequestDto>>.Success(rows.Select(c => ChangeRequestDto.From(c) with
        {
            PageId = PageOf(c),
            PipelineStageId = assignments.TryGetValue(c.AssignmentId, out var a) ? a.PipelineStageId : null,
        }).ToList());
    }
}
