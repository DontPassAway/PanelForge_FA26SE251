using MediatR;
using Microsoft.EntityFrameworkCore;
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
    Guid ChapterId, Guid AssignmentId, Guid UserId, string? Comment, IReadOnlyList<ChangeRequestItemRequest>? Changes = null,
    Guid? ExpectedSubmissionId = null)
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

        // Vùng khoanh gắn phiên bản artwork: phiên bản đó phải thuộc Chapter này
        var artworkIds = (cmd.Changes ?? []).Where(c => c.AssetReferenceId is not null)
            .Select(c => c.AssetReferenceId!.Value).Distinct().ToList();
        if (artworkIds.Count > 0 &&
            await _db.AssetReferences.CountAsync(r => artworkIds.Contains(r.Id) && r.ChapterId == assignment.ChapterId, ct) != artworkIds.Count)
            return Result<AssignmentDto>.Failure("Phiên bản artwork của yêu cầu sửa không thuộc Chapter này.", ResultErrorCodes.Validation);

        // Panel của vùng khoanh phải thuộc một Page của Chapter này
        var panelIds = (cmd.Changes ?? []).Where(c => c.PanelId is not null).Select(c => c.PanelId!.Value).Distinct().ToList();
        if (panelIds.Count > 0)
        {
            var pageIds = await _db.Pages.Where(p => p.ChapterId == assignment.ChapterId).Select(p => p.Id).ToListAsync(ct);
            if (await _db.Panels.CountAsync(p => panelIds.Contains(p.Id) && pageIds.Contains(p.PageId), ct) != panelIds.Count)
                return Result<AssignmentDto>.Failure("Panel của yêu cầu sửa không thuộc Chapter này.", ResultErrorCodes.Validation);
        }

        if (await ChangeRequestRules.SubmissionMismatchAsync(_db, assignment.Id, cmd.ExpectedSubmissionId, ct) is { } stale)
            return Result<AssignmentDto>.Failure(stale, ResultErrorCodes.Conflict);

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
