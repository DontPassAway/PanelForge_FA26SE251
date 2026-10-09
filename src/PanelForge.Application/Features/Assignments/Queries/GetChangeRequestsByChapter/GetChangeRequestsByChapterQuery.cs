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
        return Result<IReadOnlyList<ChangeRequestDto>>.Success(rows.Select(ChangeRequestDto.From).ToList());
    }
}
