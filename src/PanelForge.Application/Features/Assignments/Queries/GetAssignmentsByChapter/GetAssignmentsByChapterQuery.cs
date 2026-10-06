using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Queries.GetAssignmentsByChapter;

/// <summary>Danh sách việc của Chapter (UC-05, UC-16). Lọc tuỳ chọn theo người nhận và trạng thái.</summary>
public sealed record GetAssignmentsByChapterQuery(
    Guid ChapterId,
    Guid? AssigneeUserId = null,
    AssignmentStatus? Status = null
) : IRequest<Result<IReadOnlyList<AssignmentDto>>>;

public sealed class GetAssignmentsByChapterQueryHandler
    : IRequestHandler<GetAssignmentsByChapterQuery, Result<IReadOnlyList<AssignmentDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetAssignmentsByChapterQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<AssignmentDto>>> Handle(GetAssignmentsByChapterQuery q, CancellationToken ct)
    {
        if (!await _db.Chapters.AsNoTracking().AnyAsync(c => c.Id == q.ChapterId, ct))
            return Result<IReadOnlyList<AssignmentDto>>.Failure($"Chapter {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var query = _db.Assignments.AsNoTracking().Where(a => a.ChapterId == q.ChapterId);
        if (q.AssigneeUserId is { } assignee) query = query.Where(a => a.AssigneeUserId == assignee);
        if (q.Status is { } status) query = query.Where(a => a.Status == status);

        var items = await query
            .OrderBy(a => a.DueDate == null).ThenBy(a => a.DueDate).ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);

        return Result<IReadOnlyList<AssignmentDto>>.Success(items.Select(AssignmentDto.From).ToList());
    }
}
