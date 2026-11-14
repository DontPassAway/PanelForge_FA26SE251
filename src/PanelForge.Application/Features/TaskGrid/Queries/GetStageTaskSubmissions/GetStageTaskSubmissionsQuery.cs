using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.TaskGrid.Models;

namespace PanelForge.Application.Features.TaskGrid.Queries.GetStageTaskSubmissions;

/// <summary>Lịch sử các lần nộp của một ô, mới nhất trước (BR-01).</summary>
public sealed record GetStageTaskSubmissionsQuery(Guid ChapterId, Guid TaskId) : IRequest<Result<IReadOnlyList<StageTaskSubmissionDto>>>;

public sealed class GetStageTaskSubmissionsQueryHandler
    : IRequestHandler<GetStageTaskSubmissionsQuery, Result<IReadOnlyList<StageTaskSubmissionDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetStageTaskSubmissionsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<StageTaskSubmissionDto>>> Handle(GetStageTaskSubmissionsQuery q, CancellationToken ct)
    {
        if (!await _db.StageTasks.AsNoTracking().AnyAsync(t => t.Id == q.TaskId && t.ChapterId == q.ChapterId, ct))
            return Result<IReadOnlyList<StageTaskSubmissionDto>>.Failure($"Ô {q.TaskId} không thuộc chương này.", ResultErrorCodes.NotFound);

        var rows = await _db.StageTaskSubmissions.AsNoTracking()
            .Where(s => s.StageTaskId == q.TaskId)
            .OrderByDescending(s => s.AttemptNumber)
            .ToListAsync(ct);
        return Result<IReadOnlyList<StageTaskSubmissionDto>>.Success(rows.Select(s => new StageTaskSubmissionDto(
            s.Id, s.AttemptNumber, s.Status.ToString(), s.SubmittedByUserId, s.SubmittedAt, s.ArtworkReferenceIds,
            s.DecidedByUserId, s.DecidedAt, s.DecisionComment)).ToList());
    }
}
