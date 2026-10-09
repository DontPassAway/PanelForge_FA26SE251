using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.OpenAssignmentReview;

/// <summary>CF3 bước 2: Editor mở việc đã nộp. Assignment Submitted → UnderReview, StageProgress của các Panel Submitted → InReview.</summary>
public sealed record OpenAssignmentReviewCommand(Guid ChapterId, Guid AssignmentId, Guid UserId)
    : IRequest<Result<AssignmentDto>>;

public sealed class OpenAssignmentReviewCommandHandler : IRequestHandler<OpenAssignmentReviewCommand, Result<AssignmentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public OpenAssignmentReviewCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AssignmentDto>> Handle(OpenAssignmentReviewCommand cmd, CancellationToken ct)
    {
        var loaded = await ReviewRules.LoadAsync(_db, cmd.ChapterId, cmd.AssignmentId, cmd.UserId, AssignmentStatus.Submitted, ct);
        if (!loaded.IsSuccess)
            return Result<AssignmentDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var assignment = loaded.Value!;
        assignment.OpenReview();

        if (await StageProgressRules.TransitionAsync(_db, assignment, r => r.OpenReview(), ct) is { } error)
            return Result<AssignmentDto>.Failure(error, ResultErrorCodes.Conflict);

        assignment.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<AssignmentDto>.Success(AssignmentDto.From(assignment));
    }
}
