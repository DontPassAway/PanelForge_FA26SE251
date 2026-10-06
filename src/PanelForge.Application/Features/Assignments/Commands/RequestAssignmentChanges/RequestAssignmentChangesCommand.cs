using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.RequestAssignmentChanges;

/// <summary>
/// CF3 bước 7 (BR-06): trả về, comment bắt buộc. Assignment UnderReview → ReworkRequired, StageProgress InReview → ChangesRequested.
/// Lý do lưu ở LastFeedbackComment; ChangeRequest/Annotation có vùng sẽ là thực thể riêng ở đợt sau.
/// </summary>
public sealed record RequestAssignmentChangesCommand(
    Guid ChapterId, Guid AssignmentId, Guid UserId, string? Comment, IReadOnlyList<ChangeRequestItemRequest>? Changes = null)
    : IRequest<Result<AssignmentDto>>;

public sealed class RequestAssignmentChangesCommandHandler : IRequestHandler<RequestAssignmentChangesCommand, Result<AssignmentDto>>
{
    private readonly IPanelForgeDbContext _db;

    public RequestAssignmentChangesCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<AssignmentDto>> Handle(RequestAssignmentChangesCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Comment))
            return Result<AssignmentDto>.Failure("Trả về bắt buộc phải có comment.", ResultErrorCodes.Validation);

        if (ChangeRequestRules.ValidateItems(cmd.Changes) is { } itemError)
            return Result<AssignmentDto>.Failure(itemError, ResultErrorCodes.Validation);

        var loaded = await ReviewRules.LoadAsync(_db, cmd.ChapterId, cmd.AssignmentId, cmd.UserId, AssignmentStatus.UnderReview, ct);
        if (!loaded.IsSuccess)
            return Result<AssignmentDto>.Failure(loaded.ErrorMessage!, loaded.ErrorCode);

        var assignment = loaded.Value!;

        if (await StageProgressRules.TransitionAsync(_db, assignment, r => r.RequestChanges(), ct) is { } error)
            return Result<AssignmentDto>.Failure(error, ResultErrorCodes.Conflict);

        // Lần nộp bị từ chối kèm comment; mỗi yêu cầu sửa gắn đúng lần nộp đó (CF3 bước 7)
        var submission = await ChangeRequestRules.LatestPendingSubmissionAsync(_db, assignment.Id, ct);
        submission?.Reject(cmd.UserId, cmd.Comment);

        assignment.RequestRework(cmd.Comment);
        ChangeRequestRules.AddChangeRequests(_db, assignment, submission?.Id, cmd.UserId, cmd.Changes, cmd.Comment);
        assignment.UpdatedBy = cmd.UserId.ToString();
        _db.Notifications.Add(AssignmentNotifications.ChangesRequested(assignment));

        await _db.SaveChangesAsync(ct);
        return Result<AssignmentDto>.Success(AssignmentDto.From(assignment));
    }
}
