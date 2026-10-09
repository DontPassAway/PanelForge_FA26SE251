using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Models;

namespace PanelForge.Application.Features.Assignments.Queries.GetAssignmentSubmissions;

/// <summary>Lịch sử các lần nộp của một việc, lần nộp đầu tiên trước (CF3, UC-09 so sánh với bản trước).</summary>
public sealed record GetAssignmentSubmissionsQuery(Guid ChapterId, Guid AssignmentId)
    : IRequest<Result<IReadOnlyList<SubmissionDto>>>;

public sealed class GetAssignmentSubmissionsQueryHandler
    : IRequestHandler<GetAssignmentSubmissionsQuery, Result<IReadOnlyList<SubmissionDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetAssignmentSubmissionsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<SubmissionDto>>> Handle(GetAssignmentSubmissionsQuery q, CancellationToken ct)
    {
        // Lọc theo ChapterId của route để chặn IDOR giữa các Workspace
        if (!await _db.Assignments.AsNoTracking().AnyAsync(a => a.Id == q.AssignmentId && a.ChapterId == q.ChapterId, ct))
            return Result<IReadOnlyList<SubmissionDto>>.Failure($"Assignment {q.AssignmentId} không tồn tại.", ResultErrorCodes.NotFound);

        var rows = await _db.Submissions.AsNoTracking()
            .Where(s => s.AssignmentId == q.AssignmentId)
            .OrderBy(s => s.AttemptNumber)
            .ToListAsync(ct);

        return Result<IReadOnlyList<SubmissionDto>>.Success(rows.Select(SubmissionDto.From).ToList());
    }
}
